namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Resolves the tenant of an anonymous public request from
/// the request host or, in development, from the tenant slug header.
/// Implemented by the Platform module against the global (non-RLS) host table.
/// </summary>
public interface ITenantHostResolver
{
    Task<int?> ResolveByHostAsync(string host, CancellationToken ct = default);
    Task<int?> ResolveBySlugAsync(string slug, CancellationToken ct = default);
}

public interface ITenantAccessPolicy
{
    Task<bool> CanAccessAsync(int userId, int tenantId, bool isPlatformAdmin, CancellationToken ct = default);
}

/// <summary>
/// Decides whether a staff user may operate the branch named in the
/// <c>X-Branch-Id</c> header. Implemented by the Iam module. Runs after the
/// tenant is bound, so the implementation's queries are RLS-filtered.
/// </summary>
public interface IBranchAccessPolicy
{
    Task<bool> CanUseBranchAsync(int userId, int branchId, CancellationToken ct = default);
}

public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Accept the <c>X-Tenant</c> slug header on public routes. Development only:
    /// production resolves public requests from the host name.
    /// </summary>
    public bool AllowTenantHeader { get; set; }

    /// <summary>Route prefix of anonymous, host-resolved endpoints.</summary>
    public string PublicPathPrefix { get; set; } = "/api/public";

}

public static class TenancyHeaders
{
    public const string Tenant = "X-Tenant";
    public const string Branch = "X-Branch-Id";
}
