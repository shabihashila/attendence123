namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// The caller's tenant, user and branch for the current request (or for the
/// current background-work scope). Bound to every SQL connection as session
/// context, which is what row-level security filters on.
/// </summary>
public interface ITenantContext
{
    int? TenantId { get; }
    int? UserId { get; }
    int? BranchId { get; }

    /// <summary>Platform operator. Grants read-only cross-tenant platform lists; writes stay tenant-bound.</summary>
    bool IsPlatformAdmin { get; }

    /// <summary>How the tenant was resolved for this request.</summary>
    TenantSource Source { get; }

    bool HasTenant => TenantId is > 0;

    /// <summary>The bound tenant, or an exception when the request has none.</summary>
    int RequiredTenantId => TenantId is > 0 ? TenantId.Value
        : throw new InvalidOperationException("No tenant is bound to the current request.");
}

public enum TenantSource
{
    None = 0,
    StaffToken = 1,
    Host = 3,
    DevelopmentHeader = 4,
    BackgroundScope = 5
}

/// <summary>Mutable, scoped implementation. Only the tenancy middleware and background scopes set it.</summary>
public sealed class TenantContext : ITenantContext
{
    public int? TenantId { get; private set; }
    public int? UserId { get; private set; }
    public int? BranchId { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public TenantSource Source { get; private set; }

    private bool _bound;

    /// <summary>Binds the context once per scope. A second bind is a programming error.</summary>
    public void Bind(int? tenantId, int? userId, int? branchId, bool isPlatformAdmin, TenantSource source)
    {
        if (_bound)
            throw new InvalidOperationException("The tenant context is already bound for this scope.");
        TenantId = tenantId is > 0 ? tenantId : null;
        UserId = userId is > 0 ? userId : null;
        BranchId = branchId is > 0 ? branchId : null;
        IsPlatformAdmin = isPlatformAdmin;
        Source = source;
        _bound = true;
    }
}
