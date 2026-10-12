using System.ComponentModel.DataAnnotations;
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
using Shared.Time;

namespace FegHR.Modules.Iam.Features.Me;

internal sealed record MeUser(int Id, string DisplayName, string? Email, string? Phone);

internal sealed record ChangePasswordRequest(
    [property: Required, StringLength(128)] string CurrentPassword,
    [property: Required, StringLength(128)] string NewPassword);

/// <summary>Everything the shell needs after sign-in: who, where, what they may do, and which branches.</summary>
internal sealed record MeResponse(
    MeUser User,
    TenantSummary? Tenant,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<BranchSummary> Branches,
    bool IsPlatformAdmin);

/// <summary>One sidebar entry. Groups have children and no route; leaves have a route.</summary>
internal sealed record NavItem(string Code, string Title, string? Icon, string? Route, IReadOnlyList<NavItem> Children);

internal static class MeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/me", async (ITenantContext tenant, IamProcedures procedures, IPermissionProvider permissions,
                ITenantDirectory tenants, BranchAccessPolicy branches, CancellationToken ct) =>
            {
                var userId = tenant.UserId!.Value;
                var user = await procedures.GetUserAsync(userId, ct);
                if (user is not { IsActive: true })
                    return ApiResults.Fail("Your account is no longer active.", StatusCodes.Status401Unauthorized);

                var granted = await permissions.GetPermissionsAsync(tenant.RequiredTenantId, userId, ct);
                var allowedBranches = await branches.GetAllowedBranchIdsAsync(userId, ct);
                var branchList = (await tenants.GetBranchesAsync(activeOnly: true, ct))
                    .Where(o => allowedBranches.Contains(o.Id)).ToList();

                return ApiResults.Ok(new MeResponse(
                    new MeUser(user.Id, user.DisplayName, user.Email, user.Phone),
                    await tenants.GetCurrentAsync(ct),
                    granted.Order(StringComparer.Ordinal).ToList(),
                    branchList,
                    user.IsPlatformAdmin));
            })
            .RequireStaff()
            .SkipLicenseCheck()
            .WithName("GetMe");

        // Self-service password change. Every other session of this membership is signed out.
        group.MapPost("/me/change-password", async (ChangePasswordRequest request, ITenantContext tenant, HttpContext http,
                IamProcedures procedures, IamDbContext db, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var userId = tenant.UserId!.Value;
                if (!PasswordHasher.Verify(request.CurrentPassword, await procedures.GetPasswordHashAsync(userId, ct)))
                    return ApiResults.Fail("The current password is not correct.", StatusCodes.Status400BadRequest,
                        new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is not correct."] });
                if (PasswordHasher.Validate(request.NewPassword) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status400BadRequest,
                        new Dictionary<string, string[]> { ["newPassword"] = [problem] });
                var now = clock.UtcNowOffset();
                await procedures.SetPasswordAsync(userId, PasswordHasher.Hash(request.NewPassword), now, ct);
                if (http.User.TenantUserId() is { } tenantUserId)
                    await AuthService.RevokeAllAsync(db, tenantUserId, now, ct);
                return ApiResults.Ok("Password changed. Please sign in again on your other devices.");
            })
            .Validate<ChangePasswordRequest>()
            .RequireStaff()
            .SkipLicenseCheck()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("ChangePassword");

        group.MapGet("/navigation", async (ITenantContext tenant, IamProcedures procedures,
                ITenantDirectory tenants, CancellationToken ct) =>
            {
                var features = await tenants.GetFeatureCodesAsync(ct);
                var rows = await procedures.GetNavigationAsync(tenant.UserId!.Value, features, ct);
                return ApiResults.Ok(BuildTree(rows));
            })
            .RequireStaff()
            .SkipLicenseCheck()
            .WithName("GetNavigation");
    }

    /// <summary>
    /// Flat procedure rows → tree. Rows arrive sorted; a group whose children
    /// were all filtered out is dropped, so the sidebar never shows empty headings.
    /// </summary>
    internal static IReadOnlyList<NavItem> BuildTree(IReadOnlyList<MenuRecord> rows)
    {
        var byParent = rows.ToLookup(r => r.ParentId);

        List<NavItem> Children(int? parentId) => byParent[parentId]
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .Select(r => new NavItem(r.Code, r.Title, r.Icon, r.Route, Children(r.Id)))
            .Where(n => n.Route is not null || n.Children.Count > 0)
            .ToList();

        return Children(null);
    }
}
