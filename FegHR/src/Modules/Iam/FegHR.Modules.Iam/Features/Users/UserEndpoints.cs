using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Iam.Domain;
using FegHR.Modules.Iam.Features.Auth;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Iam.Security;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Shared.Time;

namespace FegHR.Modules.Iam.Features.Users;

/// <summary><see cref="IsProductOwner"/>: the platform's own owner account, managed by deployment, not by the company.</summary>
internal sealed record MemberDto(int TenantUserId, int UserId, string DisplayName, string? Email, string? Phone,
    bool IsActive, IReadOnlyList<int> RoleIds, IReadOnlyList<int> BranchIds, bool IsProductOwner);

/// <summary>
/// Adds a staff member. When the email or phone already belongs to a user
/// (who works at another company), that user is linked and
/// <see cref="Password"/> is ignored; otherwise a password is required.
/// An empty <see cref="BranchIds"/> means every branch.
/// </summary>
internal sealed record CreateMemberRequest(
    [property: Required, StringLength(150, MinimumLength = 2)] string DisplayName,
    [property: EmailAddress, StringLength(254)] string? Email,
    [property: StringLength(30)] string? Phone,
    [property: StringLength(128)] string? Password,
    [property: Required] IReadOnlyList<int> RoleIds,
    IReadOnlyList<int>? BranchIds);

internal sealed record UpdateMemberRequest(
    [property: Required] IReadOnlyList<int> RoleIds,
    IReadOnlyList<int>? BranchIds,
    bool IsActive = true);

internal static class UserEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users");

        users.MapGet("/", async (IamProcedures procedures, IamDbContext db, CancellationToken ct) =>
            {
                var members = await procedures.ListMembersAsync(ct);
                var roles = await db.UserRoles.AsNoTracking().Where(ur => ur.IsActive).ToListAsync(ct);
                var branches = await db.UserBranches.AsNoTracking().Where(uo => uo.IsActive).ToListAsync(ct);
                return ApiResults.Ok(members.Select(m => new MemberDto(m.TenantUserId, m.UserId, m.DisplayName, m.Email,
                        m.Phone, m.IsActive,
                        roles.Where(r => r.TenantUserId == m.TenantUserId).Select(r => r.RoleId).ToList(),
                        branches.Where(o => o.TenantUserId == m.TenantUserId).Select(o => o.BranchId).ToList(),
                        m.IsPlatformAdmin))
                    .OrderBy(m => m.DisplayName).ToList());
            })
            .RequirePermission(IamPermissions.UsersRead)
            .WithName("ListMembers");

        users.MapPost("/", async (CreateMemberRequest request, IamProcedures procedures, IamDbContext db,
                ITenantDirectory tenants, ITenantContext tenant, PermissionGenerations generations, IAppTimeProvider clock,
                DelegationGuard guard, CancellationToken ct) =>
            {
                if (await guard.CheckMemberChangeAsync(null, request.RoleIds, ct) is { } denied)
                    return ApiResults.Fail(denied, StatusCodes.Status403Forbidden);
                var email = LoginNormalizer.NormalizeEmail(request.Email);
                var phone = LoginNormalizer.NormalizePhone(request.Phone);
                if (email is null && phone is null)
                    return ApiResults.Fail("Enter an email address or a valid mobile number.");

                var validation = await ValidateAssignmentsAsync(db, tenants, request.RoleIds, request.BranchIds, ct);
                if (validation is not null)
                    return ApiResults.Fail(validation);

                var existing = await procedures.GetLoginAsync(email ?? phone!, ct)
                               ?? (email is not null && phone is not null ? await procedures.GetLoginAsync(phone, ct) : null);
                if (existing is null && PasswordHasher.Validate(request.Password) is { } passwordProblem)
                    return ApiResults.Fail(passwordProblem, StatusCodes.Status400BadRequest,
                        new Dictionary<string, string[]> { ["password"] = [passwordProblem] });

                var now = clock.UtcNowOffset();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var sql = IamProcedures.OnContext(db);
                var userId = existing?.Id ?? await sql.CreateUserAsync(request.Email?.Trim(), phone,
                    request.DisplayName.Trim(), PasswordHasher.Hash(request.Password!), now, ct);
                if (await db.TenantUsers.AnyAsync(tu => tu.UserId == userId, ct))
                    return ApiResults.Fail("This person is already on your staff list.", StatusCodes.Status409Conflict);

                var tenantUserId = await sql.AddMembershipAsync(userId, now, ct);
                db.UserRoles.AddRange(request.RoleIds.Distinct().Select(r => new UserRole { TenantUserId = tenantUserId, RoleId = r }));
                db.UserBranches.AddRange((request.BranchIds ?? []).Distinct()
                    .Select(o => new UserBranch { TenantUserId = tenantUserId, BranchId = o }));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                generations.Invalidate(tenant.RequiredTenantId);

                return ApiResults.Created($"/api/iam/users/{tenantUserId}", new { tenantUserId, userId, linkedExisting = existing is not null });
            })
            .Validate<CreateMemberRequest>()
            .RequirePermission(IamPermissions.UsersWrite)
            .WithName("CreateMember");

        users.MapPut("/{tenantUserId:int}", async (int tenantUserId, UpdateMemberRequest request, IamDbContext db,
                IamProcedures procedures, ITenantDirectory tenants, ITenantContext tenant, PermissionGenerations generations,
                IAppTimeProvider clock, DelegationGuard guard, CancellationToken ct) =>
            {
                var member = await db.TenantUsers.SingleOrDefaultAsync(tu => tu.Id == tenantUserId, ct);
                if (member is null)
                    return ApiResults.NotFound("The staff member");
                // The product owner reaches every company through deployment; a company cannot edit or
                // remove that access, and it never stands in for the company's own owner.
                var productOwners = (await procedures.ListMembersAsync(ct)).Where(m => m.IsPlatformAdmin)
                    .Select(m => m.TenantUserId).ToHashSet();
                if (productOwners.Contains(tenantUserId))
                    return ApiResults.Fail("The product owner's access is managed by the platform.", StatusCodes.Status403Forbidden);
                if (await guard.CheckMemberChangeAsync(member, request.RoleIds, ct) is { } denied)
                    return ApiResults.Fail(denied, StatusCodes.Status403Forbidden);
                if (member.UserId == tenant.UserId && !request.IsActive)
                    return ApiResults.Fail("You cannot deactivate your own account.", StatusCodes.Status422UnprocessableEntity);

                var validation = await ValidateAssignmentsAsync(db, tenants, request.RoleIds, request.BranchIds, ct);
                if (validation is not null)
                    return ApiResults.Fail(validation);

                var ownerRoleId = await db.Roles.Where(r => r.Code == Role.OwnerCode).Select(r => (int?)r.Id).SingleOrDefaultAsync(ct);
                var keepsOwner = request.IsActive && ownerRoleId is { } oid && request.RoleIds.Contains(oid);
                if (!keepsOwner && ownerRoleId is { } ownerId && !await HasAnotherActiveOwnerAsync(db, ownerId, tenantUserId, productOwners, ct))
                    return ApiResults.Fail("Every company needs at least one active administrator.", StatusCodes.Status422UnprocessableEntity);

                var now = clock.UtcNowOffset();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var currentRoles = await db.UserRoles.Where(ur => ur.TenantUserId == tenantUserId).ToListAsync(ct);
                db.UserRoles.RemoveRange(currentRoles.Where(ur => !request.RoleIds.Contains(ur.RoleId)));
                db.UserRoles.AddRange(request.RoleIds.Distinct().Where(r => currentRoles.All(c => c.RoleId != r))
                    .Select(r => new UserRole { TenantUserId = tenantUserId, RoleId = r }));

                var wantedBranches = (request.BranchIds ?? []).Distinct().ToList();
                var currentBranches = await db.UserBranches.Where(uo => uo.TenantUserId == tenantUserId).ToListAsync(ct);
                db.UserBranches.RemoveRange(currentBranches.Where(uo => !wantedBranches.Contains(uo.BranchId)));
                db.UserBranches.AddRange(wantedBranches.Where(o => currentBranches.All(c => c.BranchId != o))
                    .Select(o => new UserBranch { TenantUserId = tenantUserId, BranchId = o }));
                await db.SaveChangesAsync(ct);

                if (member.IsActive != request.IsActive)
                {
                    await IamProcedures.OnContext(db).SetMembershipActiveAsync(tenantUserId, request.IsActive, now, ct);
                    if (!request.IsActive)
                        await AuthService.RevokeAllAsync(db, tenantUserId, now, ct);
                }

                await transaction.CommitAsync(ct);
                generations.Invalidate(tenant.RequiredTenantId);
                return ApiResults.Ok("Staff member updated.");
            })
            .Validate<UpdateMemberRequest>()
            .RequirePermission(IamPermissions.UsersWrite)
            .WithName("UpdateMember");
    }

    private static async Task<string?> ValidateAssignmentsAsync(IamDbContext db, ITenantDirectory tenants,
        IReadOnlyList<int> roleIds, IReadOnlyList<int>? branchIds, CancellationToken ct)
    {
        if (roleIds.Count == 0)
            return "Choose at least one role.";
        var distinctRoles = roleIds.Distinct().ToList();
        if (await db.Roles.CountAsync(r => distinctRoles.Contains(r.Id) && r.IsActive, ct) != distinctRoles.Count)
            return "One of the chosen roles does not exist or is inactive.";
        if (branchIds is { Count: > 0 })
        {
            var known = (await tenants.GetBranchesAsync(activeOnly: true, ct)).Select(o => o.Id).ToHashSet();
            if (branchIds.Any(o => !known.Contains(o)))
                return "One of the chosen branches does not exist or is inactive.";
        }
        return null;
    }

    private static Task<bool> HasAnotherActiveOwnerAsync(IamDbContext db, int ownerRoleId, int exceptTenantUserId,
        IReadOnlySet<int> productOwners, CancellationToken ct)
        => db.UserRoles.Where(ur => ur.RoleId == ownerRoleId && ur.IsActive && ur.TenantUserId != exceptTenantUserId
                                    && !productOwners.Contains(ur.TenantUserId))
            .Join(db.TenantUsers.Where(tu => tu.IsActive), ur => ur.TenantUserId, tu => tu.Id, (ur, _) => ur)
            .AnyAsync(ct);
}
