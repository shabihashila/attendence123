using FegHR.Modules.Iam.Contracts;
using FegHR.Modules.Iam.Persistence;

namespace FegHR.Modules.Iam.Security;

internal sealed class MemberDirectory(IamProcedures procedures) : IMemberDirectory
{
    public async Task<IReadOnlyList<MemberSummary>> ListAsync(CancellationToken ct = default)
        => (await procedures.ListMembersAsync(ct))
            .Where(m => !m.IsPlatformAdmin)
            .Select(m => new MemberSummary(m.UserId, m.TenantUserId, m.DisplayName, m.Email, m.Phone, m.IsActive))
            .OrderBy(m => m.DisplayName)
            .ToList();

    public async Task<MemberSummary?> FindAsync(int userId, CancellationToken ct = default)
        => (await procedures.ListMembersAsync(ct))
            .Where(m => m.UserId == userId)
            .Select(m => new MemberSummary(m.UserId, m.TenantUserId, m.DisplayName, m.Email, m.Phone, m.IsActive))
            .FirstOrDefault();
}
