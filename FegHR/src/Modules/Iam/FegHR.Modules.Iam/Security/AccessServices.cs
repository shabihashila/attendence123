using System.Collections.Concurrent;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FegHR.Modules.Iam.Security;

/// <summary>
/// Per-tenant generation counter for cached permissions. Any change to roles,
/// role permissions or memberships bumps the tenant's generation, which
/// orphans every cached entry for that tenant at once. In-memory: correct for
/// the single-instance deployment; a scale-out needs a distributed cache.
/// </summary>
internal sealed class PermissionGenerations
{
    private readonly ConcurrentDictionary<int, int> _generations = new();

    public int Current(int tenantId) => _generations.GetOrAdd(tenantId, 0);

    public void Invalidate(int tenantId) => _generations.AddOrUpdate(tenantId, 1, (_, g) => g + 1);
}

internal sealed class PermissionProvider(
    IamProcedures procedures,
    ITenantContext tenant,
    IMemoryCache cache,
    PermissionGenerations generations) : IPermissionProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async ValueTask<IReadOnlySet<string>> GetPermissionsAsync(int tenantId, int userId, CancellationToken ct = default)
    {
        // The procedure runs under the scope's RLS tenant; asking about another one is a bug.
        if (tenant.TenantId != tenantId)
            throw new InvalidOperationException("Permissions can only be read for the bound tenant.");

        var key = $"perm:{tenantId}:{generations.Current(tenantId)}:{userId}";
        if (cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
            return cached;

        var codes = (await procedures.GetPermissionsAsync(userId, ct)).ToHashSet(StringComparer.Ordinal);
        cache.Set(key, (IReadOnlySet<string>)codes, Ttl);
        return codes;
    }
}

/// <summary>
/// A member may use an branch when it is an active branch of the tenant and
/// either they have no branch restrictions or the branch is among theirs.
/// </summary>
internal sealed class BranchAccessPolicy(IamDbContext db, ITenantDirectory tenants) : IBranchAccessPolicy
{
    public async Task<bool> CanUseBranchAsync(int userId, int branchId, CancellationToken ct = default)
    {
        var allowed = await GetAllowedBranchIdsAsync(userId, ct);
        return allowed.Contains(branchId);
    }

    public async Task<IReadOnlySet<int>> GetAllowedBranchIdsAsync(int userId, CancellationToken ct)
    {
        var active = (await tenants.GetBranchesAsync(activeOnly: true, ct)).Select(o => o.Id).ToHashSet();
        var restricted = await db.TenantUsers.AsNoTracking()
            .Where(tu => tu.UserId == userId && tu.IsActive)
            .Join(db.UserBranches.Where(uo => uo.IsActive), tu => tu.Id, uo => uo.TenantUserId, (_, uo) => uo.BranchId)
            .ToListAsync(ct);
        if (restricted.Count == 0)
            return active;
        active.IntersectWith(restricted);
        return active;
    }
}
