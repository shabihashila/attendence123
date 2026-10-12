using FegHR.SharedKernel.Persistence;

namespace FegHR.Modules.Iam.Domain;

/// <summary><c>iam.TenantUsers</c>: a global user's membership of the bound tenant.</summary>
internal sealed class TenantUser : TenantEntity
{
    public int UserId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>iam.Roles</c>. System roles are seeded per tenant and cannot be deleted.</summary>
internal sealed class Role : TenantEntity
{
    /// <summary>The company administrator: holds every company permission by rule.</summary>
    public const string OwnerCode = "admin";

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public List<RolePermission> Permissions { get; set; } = [];
}

/// <summary><c>iam.RolePermissions</c>.</summary>
internal sealed class RolePermission : TenantEntity
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>iam.UserRoles</c>.</summary>
internal sealed class UserRole : TenantEntity
{
    public int TenantUserId { get; set; }
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// <c>iam.UserBranches</c>. A member with no rows may use every active
/// branch; rows restrict the member to those branches.
/// </summary>
internal sealed class UserBranch : TenantEntity
{
    public int TenantUserId { get; set; }
    public int BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>iam.RefreshTokens</c>: hashed, single-use, rotated on every refresh.</summary>
internal sealed class RefreshToken : ITenantOwned, IAuditable, IVersioned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int TenantUserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public byte[]? ReplacedByTokenHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary><c>iam.Permissions</c>: global catalog seeded by the module scripts. Read-only here.</summary>
internal sealed class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
