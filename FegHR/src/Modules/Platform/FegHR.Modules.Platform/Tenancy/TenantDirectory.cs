using FegHR.Modules.Platform.Contracts;
using FegHR.Modules.Platform.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shared.Time;

namespace FegHR.Modules.Platform.Tenancy;

internal sealed class TenantDirectory(PlatformProcedures procedures, PlatformDbContext db, IAppTimeProvider clock)
    : ITenantDirectory
{
    public Task<TenantSummary?> GetCurrentAsync(CancellationToken ct = default) => procedures.GetCurrentTenantAsync(ct);

    public Task<IReadOnlyList<string>> GetFeatureCodesAsync(CancellationToken ct = default)
        => procedures.GetFeatureCodesAsync(clock.UtcNowOffset(), ct);

    public async Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(bool activeOnly = true, CancellationToken ct = default)
        => await db.Branches.AsNoTracking()
            .Where(o => !activeOnly || o.IsActive)
            .OrderBy(o => o.Name)
            .Select(o => new BranchSummary(o.Id, o.Code, o.Name, o.IsActive))
            .ToListAsync(ct);

    public async Task<TenantClock> GetClockAsync(CancellationToken ct = default)
        => new(await db.TenantSettings.AsNoTracking().Select(s => s.TimeZoneId).FirstOrDefaultAsync(ct) ?? "Asia/Dhaka");

    public Task<LicenseStatus?> GetLicenseStatusAsync(CancellationToken ct = default)
        => procedures.GetLicenseStatusAsync(clock.UtcNowOffset(), ct);
}

/// <summary>
/// The licence check behind <see cref="LicenseGuardMiddleware"/>. A verdict is cached for a
/// minute per company (and never past the licence's own end), and dropped when the product owner
/// changes that company's licence or status.
/// </summary>
internal sealed class LicenseGate(PlatformProcedures procedures, ITenantContext tenant, IMemoryCache cache, IAppTimeProvider clock)
    : ILicenseGate
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    public static string CacheKey(int tenantId) => $"licence:{tenantId}";

    public async ValueTask<LicenseVerdict> CheckAsync(int tenantId, CancellationToken ct = default)
    {
        // The procedure reads the bound company; asking about another one is a bug.
        if (tenant.TenantId != tenantId)
            throw new InvalidOperationException("The licence can only be read for the bound company.");

        var now = clock.UtcNowOffset();
        if (cache.TryGetValue(CacheKey(tenantId), out LicenseVerdict? cached) && cached is not null
            && (cached.EndsAt is null || cached.EndsAt > now || !cached.IsLicensed))
            return cached;

        var status = await procedures.GetLicenseStatusAsync(now, ct);
        var verdict = status switch
        {
            null => new LicenseVerdict(false, "This company could not be found.", null),
            { TenantStatus: not "Active" } => new LicenseVerdict(false,
                "This company's access has been suspended. Please contact us.", null),
            { IsLicensed: true } => new LicenseVerdict(true, null, status.EndsAt),
            { EndsAt: { } ended } => new LicenseVerdict(false,
                $"Your {(status.Kind == "Trial" ? "free trial" : "licence")} ended on {ended:d MMM yyyy}. Please contact us to continue.", ended),
            _ => new LicenseVerdict(false, "This company has no active licence. Please contact us.", null)
        };
        cache.Set(CacheKey(tenantId), verdict, Ttl);
        return verdict;
    }
}
