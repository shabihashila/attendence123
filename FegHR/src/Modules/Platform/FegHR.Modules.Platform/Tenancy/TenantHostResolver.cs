using FegHR.Modules.Platform.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FegHR.Modules.Platform.Tenancy;

internal sealed class PlatformTenancyOptions
{
    /// <summary>
    /// Shared product domain, e.g. <c>feghr.app</c>: a request to
    /// <c>acme.feghr.app</c> resolves by the slug <c>acme</c>.
    /// Any other host is matched against <c>platform.TenantHosts</c> (custom domains).
    /// </summary>
    public string? RootDomain { get; set; }
}

/// <summary>
/// Resolves anonymous public requests to a company. Results, including
/// misses, are cached briefly so a burst of public traffic does not query the
/// host table on every request.
/// </summary>
internal sealed class TenantHostResolver(PlatformProcedures procedures, IMemoryCache cache,
    IOptions<PlatformTenancyOptions> options) : ITenantHostResolver
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);

    public Task<int?> ResolveByHostAsync(string host, CancellationToken ct = default)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        var root = options.Value.RootDomain?.Trim().TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(root) && normalized.EndsWith("." + root, StringComparison.Ordinal))
        {
            var slug = normalized[..^(root.Length + 1)];
            if (slug.Length > 0 && !slug.Contains('.'))
                return ResolveCachedAsync(slug, ct);
        }
        return ResolveCachedAsync(normalized, ct);
    }

    public Task<int?> ResolveBySlugAsync(string slug, CancellationToken ct = default)
        => ResolveCachedAsync(slug.Trim().ToLowerInvariant(), ct);

    private async Task<int?> ResolveCachedAsync(string key, CancellationToken ct)
    {
        if (key.Length is 0 or > 253)
            return null;
        if (cache.TryGetValue($"tenant-host:{key}", out int? cached))
            return cached;

        var tenantId = await procedures.ResolveTenantAsync(key, ct);
        cache.Set($"tenant-host:{key}", tenantId, tenantId is null ? MissTtl : HitTtl);
        return tenantId;
    }
}
