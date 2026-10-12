// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.ComponentModel.DataAnnotations;

namespace FegStorage.WebApi.Storage;

// =======================================================================
// Per-call backend config carried in every request body. The consuming
// project (HRAccounts, etc.) holds the per-tenant credentials in its own DB
// and forwards them with each call -- the file service stores nothing
// per-tenant and is fully stateless w.r.t. configuration.
// =======================================================================

// Polymorphic config block. Only the sub-DTO matching `Provider` is
// inspected; the rest are ignored. Strongly typed (instead of a flat
// IDictionary<string,string>) so Swagger surfaces the right fields and
// callers get compile-time help when generating clients.
public sealed class BackendConfig
{
    [Required] public StorageProvider Provider { get; set; }

    // Container / bucket / share name. Required for cloud backends so
    // S3/Azure/R2 know where to land the object. For Local backend this
    // becomes the first sub-directory under LocalBackend:BasePath, which
    // keeps tenant files isolated on disk the same way they are in cloud.
    [Required, MaxLength(120)] public string Container { get; set; } = string.Empty;

    public LocalBackendConfig? Local { get; set; }
    public S3BackendConfig?    S3    { get; set; }   // also used for R2 (with serviceUrl set)
    public AzureBackendConfig? Azure { get; set; }
}

public sealed class LocalBackendConfig
{
    // Override the service-wide BasePath if a particular tenant wants
    // its own directory (e.g. a mounted disk per tenant). Null = use
    // the service's configured default.
    [MaxLength(260)] public string? BasePathOverride { get; set; }
}

public sealed class S3BackendConfig
{
    [Required, MaxLength(120)] public string AccessKey { get; set; } = string.Empty;
    [Required, MaxLength(240)] public string SecretKey { get; set; } = string.Empty;
    [MaxLength(40)]            public string? Region    { get; set; }   // optional for R2

    // For R2: set to "https://<accountId>.r2.cloudflarestorage.com".
    // For S3: leave null to use the AWS endpoint resolved from Region.
    [MaxLength(240)] public string? ServiceUrl { get; set; }

    // R2 requires path-style addressing; AWS S3 defaults to virtual-hosted.
    public bool ForcePathStyle { get; set; }
}

public sealed class AzureBackendConfig
{
    // Two ways callers can authenticate. Use whichever they already have:
    //   - ConnectionString: the full "DefaultEndpointsProtocol=...;AccountKey=..." string
    //   - AccountName + AccountKey: the two pieces separately
    [MaxLength(2000)] public string? ConnectionString { get; set; }
    [MaxLength(120)]  public string? AccountName     { get; set; }
    [MaxLength(240)]  public string? AccountKey      { get; set; }
}

// =======================================================================
// Operation DTOs -- requests + results
// =======================================================================

// /api/files/upload uses multipart/form-data:
//   - file part: the bytes
//   - meta part (form field "meta"): JSON-serialized UploadMetadata
//
// We don't put the file in JSON (no base64 bloat) and don't put the
// config in a header (no header bloat / no JSON-in-header escaping
// problems).
public sealed class UploadMetadata
{
    [Required] public BackendConfig Config { get; set; } = new();

    [Required, MaxLength(500)] public string Key { get; set; } = string.Empty;
    [MaxLength(120)]           public string? ContentType { get; set; }

    // Override the service-wide default. Capped at 24h on the
    // controller side so a misconfigured caller can't issue
    // long-lived links.
    public int? ViewUrlMinutes { get; set; }
}

public sealed class UploadResult
{
    public StorageProvider Provider { get; set; }
    public string Container { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public long  SizeBytes { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string Sha256 { get; set; } = string.Empty;

    // Short-lived URL the UI can use to render <img src="..."> without
    // proxying through the consumer API. Cloud backends use the SDK's
    // presigned-URL feature; Local backend uses an HMAC-signed path
    // served by /files/view/{payload}?sig=...
    public string ViewUrl { get; set; } = string.Empty;
    public DateTimeOffset ViewUrlExpiresAt { get; set; }
}

public class FileOperationRequest
{
    [Required] public BackendConfig Config { get; set; } = new();
    [Required, MaxLength(500)] public string Key { get; set; } = string.Empty;
}

public sealed class ViewUrlRequest : FileOperationRequest
{
    public int? ViewUrlMinutes { get; set; }
}

public sealed class ViewUrlResult
{
    public string ViewUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class DeleteResult
{
    public bool Existed { get; set; }
}
