// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.Security.Cryptography;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Serilog;

namespace FegStorage.WebApi.Storage;

// Azure Blob Storage backend. Two auth modes:
//   1) ConnectionString -- "DefaultEndpointsProtocol=...;AccountKey=..."
//   2) AccountName + AccountKey -- the same two pieces separately
// The choice is purely caller convenience; either yields a
// StorageSharedKeyCredential under the hood which is what SAS issuance
// requires.
//
// Per-request lifetime, no pooling. The BlobServiceClient is cheap
// enough to construct that this is fine for HR Accounts's volume; a
// per-tenant cache can be added in the factory later if the
// audit logs show repeated cold starts.
internal sealed class AzureBlobStorageBackend : IStorageBackend
{
    private readonly BlobServiceClient _svc;
    private readonly StorageSharedKeyCredential? _sharedKey;

    public AzureBlobStorageBackend(AzureBackendConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ConnectionString))
        {
            _svc = new BlobServiceClient(cfg.ConnectionString);
            _sharedKey = ExtractSharedKey(cfg.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(cfg.AccountName) && !string.IsNullOrWhiteSpace(cfg.AccountKey))
        {
            _sharedKey = new StorageSharedKeyCredential(cfg.AccountName, cfg.AccountKey);
            var uri = new Uri($"https://{cfg.AccountName}.blob.core.windows.net");
            _svc = new BlobServiceClient(uri, _sharedKey);
        }
        else
        {
            throw new ArgumentException(
                "AzureBackendConfig: supply either ConnectionString or (AccountName + AccountKey)");
        }
    }

    public StorageProvider Provider => StorageProvider.AzureBlob;

    public async Task<StoredObjectInfo> UploadAsync(
        string container, string key, Stream content, string contentType, CancellationToken ct)
    {
        var blobs = _svc.GetBlobContainerClient(container);
        // Ensure the container exists so callers don't have to know to
        // pre-create it. CreateIfNotExistsAsync is idempotent.
        await blobs.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        // Hash + size while uploading. Azure's blob client wants a
        // seekable stream so we buffer once -- same trade-off the S3
        // backend makes. Capped at MaxUploadBytes upstream.
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, 81920, ct);
        ms.Position = 0;
        var hash = SHA256.HashData(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        ms.Position = 0;

        var blob = blobs.GetBlobClient(key);
        await blob.UploadAsync(ms, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
            }
        }, ct);

        Log.Information("AzureBlob upload OK container={Container} key={Key} bytes={Bytes}", container, key, ms.Length);
        return new StoredObjectInfo
        {
            SizeBytes   = ms.Length,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Sha256      = Convert.ToHexString(hash).ToLowerInvariant()
        };
    }

    public async Task<DownloadedObject> DownloadAsync(string container, string key, CancellationToken ct)
    {
        var blob = _svc.GetBlobContainerClient(container).GetBlobClient(key);
        try
        {
            var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
            return new DownloadedObject
            {
                Content     = resp.Value.Content,
                ContentType = string.IsNullOrWhiteSpace(resp.Value.Details.ContentType) ? "application/octet-stream" : resp.Value.Details.ContentType,
                SizeBytes   = resp.Value.Details.ContentLength <= 0 ? null : resp.Value.Details.ContentLength
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new KeyNotFoundException($"Object not found: {container}/{key}");
        }
    }

    public async Task<bool> DeleteAsync(string container, string key, CancellationToken ct)
    {
        var blob = _svc.GetBlobContainerClient(container).GetBlobClient(key);
        var resp = await blob.DeleteIfExistsAsync(cancellationToken: ct);
        return resp.Value;   // true if existed and was deleted, false if it was already absent
    }

    public Task<ViewUrlResult> GetViewUrlAsync(string container, string key, TimeSpan ttl, CancellationToken ct)
    {
        if (_sharedKey is null)
            throw new InvalidOperationException(
                "Azure view URLs require shared-key credentials (ConnectionString with AccountKey, " +
                "or AccountName + AccountKey).");

        var expires = DateTimeOffset.UtcNow.Add(ttl);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = container,
            BlobName          = key,
            Resource          = "b",  // blob (vs. "c" for container)
            ExpiresOn         = expires,
            Protocol          = SasProtocol.Https
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        var blob = _svc.GetBlobContainerClient(container).GetBlobClient(key);
        var url = new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(_sharedKey)
        }.ToUri();

        return Task.FromResult(new ViewUrlResult
        {
            ViewUrl = url.ToString(),
            ExpiresAt = expires
        });
    }

    // Pulls the AccountName + AccountKey out of a connection string so
    // we can issue SAS tokens. Returns null if the conn-string uses a
    // different auth shape (SAS-only, AAD, etc.) -- in that case
    // GetViewUrlAsync will throw a clear message above.
    private static StorageSharedKeyCredential? ExtractSharedKey(string connectionString)
    {
        string? acct = null;
        string? key  = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            if (k.Equals("AccountName", StringComparison.OrdinalIgnoreCase)) acct = v;
            else if (k.Equals("AccountKey", StringComparison.OrdinalIgnoreCase)) key = v;
        }
        return (acct is not null && key is not null) ? new StorageSharedKeyCredential(acct, key) : null;
    }
}
