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
using Microsoft.Extensions.DependencyInjection;
using Shared.Time;

namespace FegHR.Modules.Iam.Features.Users;

/// <summary>Authorizes the source company before opening a target company scope.</summary>
internal static class CompanyAccessEndpoints
{
    public static void Map(RouteGroupBuilder users)
    {
        users.MapGet("/company-access/{userId:int}", async (int userId, IamProcedures procedures,
            ITenantContext caller, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            if (!(await procedures.ListMembersAsync(ct)).Any(m => m.UserId == userId))
                return ApiResults.NotFound("The staff member");
            var userCompanies = await procedures.GetTenantsAsync(userId, ct);
            var defaultId = await procedures.ResolveDefaultCompanyAsync(userId, ct);
            var result = new List<object>();
            foreach (var company in await procedures.GetManagedCompaniesAsync(caller.UserId!.Value, ct))
            {
                await using var scope = scopes.CreateTenantScope(company.TenantId, caller.UserId);
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<IamDbContext>();
                var member = (await sp.GetRequiredService<IamProcedures>().ListMembersAsync(ct))
                    .FirstOrDefault(m => m.UserId == userId);
                var roleIds = member is null ? [] : await db.UserRoles.Where(r => r.TenantUserId == member.TenantUserId && r.IsActive).Select(r => r.RoleId).ToListAsync(ct);
                var branchIds = member is null ? [] : await db.UserBranches.Where(r => r.TenantUserId == member.TenantUserId && r.IsActive).Select(r => r.BranchId).ToListAsync(ct);
                result.Add(new {
                    companyId = company.TenantId, company.Name, company.ParentTenantRefId, company.ParentName,
                    member?.TenantUserId, isActive = member?.IsActive ?? false, roleIds, branchIds,
                    isDefault = company.TenantId == defaultId,
                    canBeDefault = userCompanies.Any(t => t.TenantId == company.TenantId)
                        && !userCompanies.Any(t => t.ParentTenantRefId == company.TenantId),
                    isProtected = member?.IsPlatformAdmin == true || member?.IsGroupProtected == true,
                    roles = await db.Roles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync(ct),
                    branches = await sp.GetRequiredService<ITenantDirectory>().GetBranchesAsync(true, ct)
                });
            }
            return ApiResults.Ok(result);
        }).RequirePermission(IamPermissions.UsersWrite);

        users.MapPut("/company-access/{userId:int}/default", async (int userId, DefaultCompanyRequest request,
            IamProcedures procedures, ITenantContext caller, CancellationToken ct) =>
        {
            var known = (await procedures.ListMembersAsync(ct)).FirstOrDefault(m => m.UserId == userId);
            if (known is null) return ApiResults.NotFound("The staff member");
            if (known.IsPlatformAdmin) return ApiResults.Fail("The product owner sets their own default under My account.", 403);
            if (!(await procedures.GetManagedCompaniesAsync(caller.UserId!.Value, ct)).Any(t => t.TenantId == request.TenantId))
                return ApiResults.Fail("Choose a default within your company and its child companies.", 403);
            var memberships = await procedures.GetTenantsAsync(userId, ct);
            if (!memberships.Any(t => t.TenantId == request.TenantId))
                return ApiResults.Fail("The user must have active access to the default company.", 422);
            var id = await procedures.ResolveDefaultCompanyAsync(userId, ct, request.TenantId);
            return ApiResults.Ok(new { tenantId = id }, "User's default company saved.");
        }).Validate<DefaultCompanyRequest>().RequirePermission(IamPermissions.UsersWrite);

        users.MapPut("/company-access/{userId:int}/{companyId:int}", async (int userId, int companyId,
            UpdateMemberRequest request, IamProcedures procedures, ITenantContext caller,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var known = (await procedures.ListMembersAsync(ct)).FirstOrDefault(m => m.UserId == userId);
            if (known is null) return ApiResults.NotFound("The staff member");
            if (known.IsPlatformAdmin) return ApiResults.Fail("The product owner's access is managed by the platform.", 403);
            var authority = (await procedures.GetManagedCompaniesAsync(caller.UserId!.Value, ct))
                .FirstOrDefault(t => t.TenantId == companyId);
            if (authority is null)
                return ApiResults.Fail("You can only assign access within your company and its child companies.", 403);
            await using var scope = scopes.CreateTenantScope(companyId, caller.UserId);
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<IamDbContext>();
            var sql = sp.GetRequiredService<IamProcedures>();
            var member = await db.TenantUsers.SingleOrDefaultAsync(m => m.UserId == userId, ct);
            var directory = sp.GetRequiredService<ITenantDirectory>();
            var tenant = sp.GetRequiredService<ITenantContext>();
            var generations = sp.GetRequiredService<PermissionGenerations>();
            var clock = sp.GetRequiredService<IAppTimeProvider>();
            var guard = sp.GetRequiredService<DelegationGuard>();
            if (authority.FullAdministration) guard.UseVerifiedHierarchyAuthority(caller.RequiredTenantId);
            if (member is not null)
                return await UserEndpoints.UpdateAsync(member.Id, request, db, sql, directory, tenant, generations, clock, guard, ct);
            if (!request.IsActive) return ApiResults.Ok("No access assigned to this company.");
            var profile = await sql.GetUserAsync(userId, ct);
            if (profile is not { IsActive: true }) return ApiResults.Fail("The account is inactive.", 422);
            return await UserEndpoints.CreateAsync(new CreateMemberRequest(profile.DisplayName, profile.Email, profile.Phone,
                null, request.RoleIds, request.BranchIds), sql, db, directory, tenant, generations, clock, guard, ct);
        }).Validate<UpdateMemberRequest>().RequirePermission(IamPermissions.UsersWrite);
    }
}
