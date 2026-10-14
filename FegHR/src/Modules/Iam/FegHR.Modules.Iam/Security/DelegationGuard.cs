using FegHR.Modules.Iam.Domain;
using FegHR.Modules.Iam.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Iam.Security;

/// <summary>
/// Who may hand out which rights. A company administrator configures everything (every role, every grant, every
/// member). Anyone else holding <c>iam.roles.write</c> or <c>iam.users.write</c> can only
/// delegate what they hold themselves: they cannot touch a role they belong to, grant or manage a
/// permission they lack, assign or remove the administrator role, or change an administrator's or their own roles.
/// </summary>
internal sealed class DelegationGuard(IamDbContext db, ITenantContext tenant, IPermissionProvider permissions, IamProcedures procedures)
{
    private Caller? _caller;

    private sealed record Caller(int UserId, int TenantUserId, bool IsOwner, IReadOnlySet<string> Permissions, IReadOnlySet<int> RoleIds);

    /// <summary>Null when allowed; otherwise the message to show.</summary>
    public async Task<string?> CheckRoleChangeAsync(Role? existing, IReadOnlyCollection<string> requestedCodes, CancellationToken ct)
    {
        var caller = await CallerAsync(ct);
        if (caller.IsOwner)
            return null;
        if (existing is not null && caller.RoleIds.Contains(existing.Id))
            return "You cannot change a role you hold. Ask a company administrator.";
        var current = existing is null ? [] : await RoleCodesAsync([existing.Id], ct);
        var missing = requestedCodes.Concat(current).Where(c => !caller.Permissions.Contains(c)).Distinct().ToList();
        return missing.Count > 0
            ? $"You can only manage roles within your own rights. Missing: {string.Join(", ", missing.Order())}."
            : null;
    }

    /// <summary>Null when allowed; otherwise the message to show.</summary>
    public async Task<string?> CheckMemberChangeAsync(TenantUser? member, IReadOnlyCollection<int> requestedRoleIds, CancellationToken ct)
    {
        var caller = await CallerAsync(ct);
        if (caller.IsOwner)
            return null;
        var ownerRoleId = await db.Roles.Where(r => r.Code == Role.OwnerCode).Select(r => (int?)r.Id).SingleOrDefaultAsync(ct);
        if (ownerRoleId is { } oid && requestedRoleIds.Contains(oid))
            return "Only a company administrator can make someone an administrator.";
        IReadOnlyCollection<int> currentRoleIds = [];
        if (member is not null)
        {
            if (member.UserId == caller.UserId)
                return "You cannot change your own roles. Ask a company administrator.";
            currentRoleIds = await db.UserRoles.Where(ur => ur.TenantUserId == member.Id && ur.IsActive).Select(ur => ur.RoleId).ToListAsync(ct);
            if (ownerRoleId is { } owner && currentRoleIds.Contains(owner))
                return "Only a company administrator can change an administrator's account.";
        }
        var codes = await RoleCodesAsync(requestedRoleIds.Concat(currentRoleIds).Distinct().ToList(), ct);
        var missing = codes.Where(c => !caller.Permissions.Contains(c)).Distinct().ToList();
        return missing.Count > 0
            ? $"You can only assign roles within your own rights. Missing: {string.Join(", ", missing.Order())}."
            : null;
    }

    private async Task<Caller> CallerAsync(CancellationToken ct)
    {
        if (_caller is not null)
            return _caller;
        var tenantId = tenant.RequiredTenantId;
        var userId = tenant.UserId ?? throw new InvalidOperationException("A signed-in staff user is required.");
        var tenantUserId = await db.TenantUsers.Where(tu => tu.UserId == userId).Select(tu => tu.Id).SingleAsync(ct);
        var roleIds = (await db.UserRoles.Where(ur => ur.TenantUserId == tenantUserId && ur.IsActive).Select(ur => ur.RoleId).ToListAsync(ct))
            .ToHashSet();
        var isOwner = await procedures.IsGroupAdminAsync(userId, ct) || await db.Roles.AnyAsync(r => roleIds.Contains(r.Id) && r.Code == Role.OwnerCode && r.IsActive, ct);
        return _caller = new Caller(userId, tenantUserId, isOwner, await permissions.GetPermissionsAsync(tenantId, userId, ct), roleIds);
    }

    private async Task<IReadOnlyList<string>> RoleCodesAsync(IReadOnlyCollection<int> roleIds, CancellationToken ct)
        => await (from rp in db.RolePermissions.AsNoTracking()
                  join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                  where roleIds.Contains(rp.RoleId) && rp.IsActive
                  select p.Code).Distinct().ToListAsync(ct);
}
