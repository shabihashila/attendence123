// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using Microsoft.Extensions.Options;

namespace FegStorage.WebApi.Storage;

// Switch-by-provider factory. Each backend impl is constructed with
// just the request-side state it needs:
//   - LocalStorageBackend wants the service-wide BasePath +
//     LocalUrlSigner (for HMAC view URLs).
//   - S3 / Azure backends (Phase B) will close over the per-request
//     credentials.
//
// Designed as a singleton so the service-wide collaborators (BasePath,
// LocalUrlSigner) are captured once. Each Create() call returns a
// transient IStorageBackend wired with the request's config.
internal sealed class StorageBackendFactory(
    IOptions<LocalBackendSettings> localOpts,
    LocalUrlSigner urlSigner,
    IHostEnvironment env,
    IHttpContextAccessor http) : IStorageBackendFactory
{
    private readonly LocalBackendSettings _localDefaults = localOpts.Value;
    private readonly LocalUrlSigner _urlSigner = urlSigner;
    private readonly IHostEnvironment _env = env;
    private readonly IHttpContextAccessor _http = http;

    public IStorageBackend Create(BackendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Container))
            throw new ArgumentException("Container is required");

        return config.Provider switch
        {
            StorageProvider.Local =>
                MakeLocal(config.Local?.BasePathOverride ?? _localDefaults.BasePath),

            StorageProvider.S3 or StorageProvider.R2 =>
                BuildS3(config),

            StorageProvider.AzureBlob =>
                BuildAzure(config),

            _ => throw new ArgumentException($"Unknown storage provider: {config.Provider}")
        };
    }

    private LocalStorageBackend MakeLocal(string basePath)
    {
        var (root, clientFolder) = ResolveLocalPaths(basePath);
        return new LocalStorageBackend(root, clientFolder, _urlSigner);
    }

    private S3StorageBackend BuildS3(BackendConfig config)
    {
        if (config.S3 is null
            || string.IsNullOrWhiteSpace(config.S3.AccessKey)
            || string.IsNullOrWhiteSpace(config.S3.SecretKey))
            throw new ArgumentException(
                "Provider 'S3' / 'R2' requires config.s3.accessKey + config.s3.secretKey");

        // R2 has its own quirks (path-style addressing, custom endpoint).
        // We default ForcePathStyle = true on R2 when the caller forgot,
        // since virtual-hosted requests against R2's endpoint fail with
        // a not-helpful XML error.
        if (config.Provider == StorageProvider.R2)
        {
            if (string.IsNullOrWhiteSpace(config.S3.ServiceUrl))
                throw new ArgumentException(
                    "Provider 'R2' requires config.s3.serviceUrl " +
                    "(https://<accountId>.r2.cloudflarestorage.com)");
            config.S3.ForcePathStyle = true;
        }

        return new S3StorageBackend(config.Provider, config.S3);
    }

    // Relative BasePath values resolve against ContentRootPath, not the
    // process working directory. Under IIS in-process the two usually
    // match, but relying on CWD has bitten us before; ContentRootPath is
    // the published-app folder regardless of how IIS launches w3wp.
    // This lets the prod config use "../data" to land uploads in a
    // sibling folder of the publish root (Home/<site>/webroot/ +
    // Home/<site>/data/), so `dotnet publish` never touches user files.
    //
    // Returns the service-wide root PLUS the *client* sub-folder (e.g.
    // "HRAccounts", "HomeoCare") so each consumer's files live in their
    // own bucket — derived from the API-key-matched principal name, so
    // the caller can't write into someone else's folder. Falls back to
    // "default" when the legacy single-key path is used. Backends store
    // under {root}/{client}/{container}/{key}, and the client folder is
    // embedded in signed view URLs so the anonymous view path can
    // resolve back to the same bucket.
    private (string RootPath, string ClientFolder) ResolveLocalPaths(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException("LocalBackend:BasePath is not configured.");

        var root = Path.IsPathRooted(basePath)
            ? Path.GetFullPath(basePath)
            : Path.GetFullPath(Path.Combine(_env.ContentRootPath, basePath));

        return (root, SanitizeClientFolder(_http.HttpContext?.User?.Identity?.Name));
    }

    // Keep only characters that are safe + meaningful as a folder name.
    // Anything else collapses to "default" so a misconfigured principal
    // name can never escape the bucket via "..", drive letters, etc.
    private static string SanitizeClientFolder(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "default";
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "default" : clean;
    }

    private AzureBlobStorageBackend BuildAzure(BackendConfig config)
    {
        if (config.Azure is null
            || (string.IsNullOrWhiteSpace(config.Azure.ConnectionString)
                && (string.IsNullOrWhiteSpace(config.Azure.AccountName)
                    || string.IsNullOrWhiteSpace(config.Azure.AccountKey))))
            throw new ArgumentException(
                "Provider 'AzureBlob' requires config.azure.connectionString OR " +
                "(config.azure.accountName + config.azure.accountKey)");

        return new AzureBlobStorageBackend(config.Azure);
    }
}
