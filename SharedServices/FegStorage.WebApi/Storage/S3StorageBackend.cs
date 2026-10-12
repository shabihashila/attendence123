// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Serilog;

namespace FegStorage.WebApi.Storage;

// Serves both AWS S3 and Cloudflare R2. R2 is S3-compatible -- the only
// differences are the endpoint URL and the path-style addressing flag,
// both surfaced on S3BackendConfig. One backend impl keeps the surface
// uniform and the test matrix small.
//
// Per-request lifetime: the AmazonS3Client is built from creds in the
// inbound BackendConfig and disposed when the IStorageBackend goes out
// of scope. No per-tenant caching here; if request volume per tenant
// justifies pooling later, add it in the factory.
internal sealed class S3StorageBackend : IStorageBackend, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly StorageProvider _provider;

    public S3StorageBackend(StorageProvider provider, S3BackendConfig cfg)
    {
        _provider = provider;

        var creds = new BasicAWSCredentials(cfg.AccessKey, cfg.SecretKey);
        var s3cfg = new AmazonS3Config
        {
            // R2 doesn't support virtual-hosted style at all -- ForcePathStyle
            // is required. AWS S3 accepts either; default to virtual-hosted
            // (the SDK default) which is friendlier with TLS SNI.
            ForcePathStyle = cfg.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(cfg.ServiceUrl))
        {
            // R2 path: explicit endpoint, region is informational only
            // (R2 uses "auto" or the literal region passed by the caller).
            s3cfg.ServiceURL = cfg.ServiceUrl;
            if (!string.IsNullOrWhiteSpace(cfg.Region))
                s3cfg.AuthenticationRegion = cfg.Region;
        }
        else if (!string.IsNullOrWhiteSpace(cfg.Region))
        {
            // AWS S3 path: resolve the endpoint from the named region.
            s3cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(cfg.Region);
        }
        else
        {
            // No region + no service URL: fall back to us-east-1. Better
            // to fail loud at the cloud-side than to silently default
            // to a wrong region.
            s3cfg.RegionEndpoint = RegionEndpoint.USEast1;
        }

        _s3 = new AmazonS3Client(creds, s3cfg);
    }

    public StorageProvider Provider => _provider;

    public async Task<StoredObjectInfo> UploadAsync(
        string container, string key, Stream content, string contentType, CancellationToken ct)
    {
        // Hash + size while streaming, so the receipt we return matches
        // the bytes the backend actually persisted. We have to buffer
        // through a MemoryStream because S3's PutObjectRequest needs a
        // seekable stream when AutoCloseStream is off; for parity with
        // the Local backend (which streams) we copy chunk-by-chunk and
        // hash as we go, then re-issue the stream.
        //
        // For large payloads (>5 MB), the S3 SDK can do multipart in
        // a streaming manner; that's a Phase-B-extras optimization.
        // Today we cap at MaxUploadBytes (50 MB by default) so a
        // single PutObject is fine.
        var (sha, copy, size) = await HashAndBufferAsync(content, ct);

        try
        {
            var req = new PutObjectRequest
            {
                BucketName = container,
                Key = key,
                InputStream = copy,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
                // R2 rejects AWS's signed streaming trailer format. The payload
                // is already buffered/seekable, so send a fixed-length signed
                // body instead; retain the SDK's default chunking for AWS S3.
                UseChunkEncoding = _provider != StorageProvider.R2,
                DisablePayloadSigning = false
            };
            await _s3.PutObjectAsync(req, ct);
        }
        finally
        {
            await copy.DisposeAsync();
        }

        Log.Information("{Provider} upload OK bucket={Bucket} key={Key} bytes={Bytes}", _provider, container, key, size);
        return new StoredObjectInfo
        {
            SizeBytes   = size,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Sha256      = sha
        };
    }

    public async Task<DownloadedObject> DownloadAsync(string container, string key, CancellationToken ct)
    {
        try
        {
            var resp = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = container,
                Key = key
            }, ct);
            return new DownloadedObject
            {
                Content     = resp.ResponseStream,
                ContentType = string.IsNullOrWhiteSpace(resp.Headers.ContentType) ? "application/octet-stream" : resp.Headers.ContentType,
                SizeBytes   = resp.ContentLength <= 0 ? null : resp.ContentLength
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException($"Object not found: {container}/{key}");
        }
    }

    public async Task<bool> DeleteAsync(string container, string key, CancellationToken ct)
    {
        // DELETE is idempotent on S3/R2 -- they return 204 whether or
        // not the object existed. To answer "did it exist before this
        // call?" we'd need an extra HEAD round-trip. Skip it: callers
        // that care can ask HEAD themselves; the contract just promises
        // "object is gone after this returns."
        try
        {
            var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = container,
                Key = key
            }, ct);
            // Successful HEAD = it existed.
            _ = head;
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = container,
                Key = key
            }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<ViewUrlResult> GetViewUrlAsync(string container, string key, TimeSpan ttl, CancellationToken ct)
    {
        var expires = DateTime.UtcNow.Add(ttl);
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = container,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expires,
            Protocol = Protocol.HTTPS
        });
        return Task.FromResult(new ViewUrlResult
        {
            ViewUrl = url,
            ExpiresAt = new DateTimeOffset(expires, TimeSpan.Zero)
        });
    }

    // Reads the source stream once, computes SHA-256 + size, and
    // returns a fresh MemoryStream positioned at zero so the SDK can
    // PUT it. 50 MB worst case (capped by MaxUploadBytes); fine for RAM.
    private static async Task<(string Sha, MemoryStream Copy, long Size)> HashAndBufferAsync(
        Stream src, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await src.CopyToAsync(ms, 81920, ct);
        ms.Position = 0;
        var hash = System.Security.Cryptography.SHA256.HashData(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        ms.Position = 0;
        return (Convert.ToHexString(hash).ToLowerInvariant(), ms, ms.Length);
    }

    public void Dispose() => _s3.Dispose();
}
