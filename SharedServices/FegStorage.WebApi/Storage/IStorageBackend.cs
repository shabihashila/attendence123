// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Storage;

// Backend abstraction. One implementation per supported provider.
// Built by IStorageBackendFactory per-request because each call carries
// its own credentials -- a single tenant may even use different
// providers for different buckets if the consuming project models it
// that way.
//
// All methods take an explicit Container + Key rather than reading from
// the BackendConfig the impl was constructed with. Why: it lets a
// future operation (copy across containers) reuse the same backend
// instance without re-creating it. Today none of the methods need
// cross-container behaviour, so we just pass the request's Container
// straight through.
public interface IStorageBackend
{
    StorageProvider Provider { get; }

    Task<StoredObjectInfo> UploadAsync(
        string container,
        string key,
        Stream content,
        string contentType,
        CancellationToken ct);

    // Returns a stream the caller can copy to the HTTP response. Caller
    // owns disposal. Throws KeyNotFoundException when the object does
    // not exist (mapped to 404 by GlobalExceptionMiddleware).
    Task<DownloadedObject> DownloadAsync(
        string container,
        string key,
        CancellationToken ct);

    // Idempotent. Returns true if the object existed before this call,
    // false if it was already absent. Either way the post-condition is
    // "object is gone."
    Task<bool> DeleteAsync(
        string container,
        string key,
        CancellationToken ct);

    // Short-lived URL the UI can use directly (no service proxy hop).
    // ttl is clamped to the service-wide maximum on the controller side.
    Task<ViewUrlResult> GetViewUrlAsync(
        string container,
        string key,
        TimeSpan ttl,
        CancellationToken ct);
}

// Returned by the backend after a successful upload. The controller
// composes the final UploadResult from this + the issued ViewUrl.
public sealed class StoredObjectInfo
{
    public long  SizeBytes  { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string Sha256     { get; set; } = string.Empty;
}

public sealed class DownloadedObject : IAsyncDisposable
{
    public required Stream Content    { get; init; }
    public required string ContentType { get; init; }
    public          long?  SizeBytes  { get; init; }

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}
