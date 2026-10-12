// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.Security.Cryptography;
using Serilog;

namespace FegStorage.WebApi.Storage;

// Filesystem-backed implementation. The service-wide root is split per
// client: each (container, key) maps to {root}/{client}/{container}/{key},
// where {client} is the sanitized API-key-matched principal name, so one
// consumer's files can never collide with another's. Keys may include
// forward slashes to build subdirectory trees -- the backend creates
// them as needed -- but `..` traversal segments are rejected outright.
//
// The client folder is also embedded in every signed view URL, because
// the /files/view path is anonymous: whoever mints the URL had an API
// key, but the browser that fetches it doesn't.
//
// View URLs are HMAC-signed and served by LocalFileViewEndpoints at
// /files/view/{payload}?sig=... -- a public path requiring no API key.
internal sealed class LocalStorageBackend(
    string rootPath,
    string clientFolder,
    LocalUrlSigner urlSigner) : IStorageBackend
{
    public StorageProvider Provider => StorageProvider.Local;

    public async Task<StoredObjectInfo> UploadAsync(
        string container, string key, Stream content, string contentType, CancellationToken ct)
    {
        var full = ResolveAndGuard(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // Stream to a temp file alongside the target so a crash mid-
        // write doesn't leave a partial object at the real path -- atomic
        // move at the end. Same pattern HRAccounts's photo-upload uses.
        var tmp = full + ".tmp-" + Guid.NewGuid().ToString("N");
        long bytes;
        byte[] hash;
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int read;
                bytes = 0;
                while ((read = await content.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    bytes += read;
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = sha.Hash!;
            }

            // Replace any pre-existing object at the target path.
            File.Move(tmp, full, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* nothing actionable */ }
            throw;
        }

        Log.Information("Local upload OK container={Container} key={Key} bytes={Bytes}", container, key, bytes);
        return new StoredObjectInfo
        {
            SizeBytes   = bytes,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Sha256      = Convert.ToHexString(hash).ToLowerInvariant()
        };
    }

    public Task<DownloadedObject> DownloadAsync(string container, string key, CancellationToken ct)
    {
        var full = ResolveAndGuard(container, key);
        if (!File.Exists(full))
            throw new KeyNotFoundException($"Object not found: {container}/{key}");

        var fi = new FileInfo(full);
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(new DownloadedObject
        {
            Content     = stream,
            ContentType = GuessContentType(key),
            SizeBytes   = fi.Length
        });
    }

    public Task<bool> DeleteAsync(string container, string key, CancellationToken ct)
    {
        var full = ResolveAndGuard(container, key);
        if (!File.Exists(full)) return Task.FromResult(false);
        File.Delete(full);
        return Task.FromResult(true);
    }

    public Task<ViewUrlResult> GetViewUrlAsync(string container, string key, TimeSpan ttl, CancellationToken ct)
    {
        var (url, expires) = urlSigner.Sign(clientFolder, container, key, ttl);
        return Task.FromResult(new ViewUrlResult { ViewUrl = url, ExpiresAt = expires });
    }

    // Maps (container, key) to an absolute path under this client's
    // bucket and guards against traversal segments. The .. check runs on
    // the *combined* relative path so a clever container name can't
    // escape either.
    private string ResolveAndGuard(string container, string key)
    {
        if (string.IsNullOrWhiteSpace(container)) throw new ArgumentException("Container is required");
        if (string.IsNullOrWhiteSpace(key))       throw new ArgumentException("Key is required");

        // Normalize to forward-slashes, reject empty / traversal segments.
        var combined = (clientFolder + "/" + container.Trim('/', '\\') + "/" + key.Trim('/', '\\'))
            .Replace('\\', '/');
        foreach (var seg in combined.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(seg) || seg == "." || seg == "..")
                throw new ArgumentException($"Invalid key segment '{seg}' in {container}/{key}");
        }

        var root = Path.GetFullPath(rootPath);
        var full = Path.GetFullPath(Path.Combine(root, combined.Replace('/', Path.DirectorySeparatorChar)));

        // Belt-and-braces: even after normalization, ensure the resolved
        // absolute path is still under the root. Catches edge cases like
        // symlinks the .. check would miss.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resolved path escapes the storage root");

        return full;
    }

    // Tiny extension-based content-type map. The "right" answer for a
    // production system is to sniff with libmagic, but for HR Accounts's
    // file set (jpg/png/pdf/xlsx/docx) the extension is reliable.
    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".pdf"            => "application/pdf",
            ".csv"            => "text/csv",
            ".txt"            => "text/plain",
            ".json"           => "application/json",
            ".xlsx"           => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx"           => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _                 => "application/octet-stream"
        };
    }
}
