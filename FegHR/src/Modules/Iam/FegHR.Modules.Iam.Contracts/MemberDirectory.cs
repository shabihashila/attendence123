namespace FegHR.Modules.Iam.Contracts;

/// <summary>A user's membership of the bound company, as other modules see it.</summary>
public sealed record MemberSummary(int UserId, int TenantUserId, string DisplayName, string? Email, string? Phone, bool IsActive);

/// <summary>
/// The sign-ins of the bound company. Organization uses it to link an employee record to the
/// person's own account; no other module reads <c>iam</c> tables.
/// </summary>
public interface IMemberDirectory
{
    /// <summary>Members of the bound company, without the product owner's support memberships.</summary>
    Task<IReadOnlyList<MemberSummary>> ListAsync(CancellationToken ct = default);

    Task<MemberSummary?> FindAsync(int userId, CancellationToken ct = default);
}
