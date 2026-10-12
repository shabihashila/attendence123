// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Storage;

// Service-wide defaults for the Local backend. Bound from the
// "LocalBackend" config section. Per-request BackendConfig.Local can
// override BasePath but the signing key + view base URL are fixed at
// the service level -- they're operational secrets that don't belong
// in caller payloads.
public sealed class LocalBackendSettings
{
    // Where files land on disk. Relative paths resolve against
    // ContentRootPath. Each container becomes a sub-directory.
    public string BasePath { get; set; } = "App_Data/storage";

    // The public-facing scheme://host the service is reachable at,
    // used as the prefix of the view URL we hand back to callers.
    // Production deploy will set this to e.g. https://files.hraccounts.com.
    public string ViewBaseUrl { get; set; } = string.Empty;

    // HMAC secret for view-URL signatures. 32+ bytes recommended. A
    // rotation here invalidates all outstanding view URLs -- which is
    // the right behaviour if the key was leaked.
    public string SigningKey { get; set; } = string.Empty;

    // Default expiry for view URLs (cloud backends use the same value).
    // Each request can override via UploadMetadata.ViewUrlMinutes;
    // controller clamps to 24h.
    public int DefaultViewUrlMinutes { get; set; } = 30;
}
