using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Iam.Domain;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Iam.Security;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Iam.Features.Roles;

internal sealed record RoleDto(int Id, string Code, string Name, bool IsSystem, bool IsActive,
    IReadOnlyList<string> PermissionCodes, string RowVersion);

internal sealed record PermissionDto(string Code, string Name, string Module);

internal sealed record SaveRoleRequest(
    [property: Required, StringLength(50, MinimumLength = 2), RegularExpression("^[a-z][a-z0-9-]*$",
        ErrorMessage = "Use lowercase letters, digits and dashes.")] string Code,
    [property: Required, StringLength(100, MinimumLength = 2)] string Name,
    [property: Required] IReadOnlyList<string> PermissionCodes,
    bool IsActive = true,
    string? RowVersion = null);

internal static class RoleEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/permissions", async (IamDbContext db, CancellationToken ct) =>
                // Product-owner permissions are never a company's to hand out.
                ApiResults.Ok(await db.Permissions.AsNoTracking().Where(p => p.IsActive && p.Module != "owner" && p.Module != "group")
                    .OrderBy(p => p.Module).ThenBy(p => p.Code)
                    .Select(p => new PermissionDto(p.Code, p.Name, p.Module)).ToListAsync(ct)))
            .RequirePermission(IamPermissions.PermissionsRead, IamPermissions.RolesWrite)
            .WithName("ListPermissions");

        var roles = group.MapGroup("/roles");

        roles.MapGet("/", async (IamDbContext db, CancellationToken ct) =>
            {
                var codes = await PermissionCodesById(db, ct);
                var list = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct);
                return ApiResults.Ok(list.Select(r => ToDto(r, codes)).ToList());
            })
            .RequirePermission(IamPermissions.RolesRead)
            .WithName("ListRoles");

        roles.MapPost("/", async (SaveRoleRequest request, IamDbContext db, ITenantContext tenant,
                PermissionGenerations generations, DelegationGuard guard, CancellationToken ct) =>
            {
                if (await guard.CheckRoleChangeAsync(null, request.PermissionCodes, ct) is { } denied)
                    return ApiResults.Fail(denied, StatusCodes.Status403Forbidden);
                if (request.Code == Role.OwnerCode || await db.Roles.AnyAsync(r => r.Code == request.Code, ct))
                    return ApiResults.Fail("A role with this code already exists.", StatusCodes.Status409Conflict);
                var resolved = await ResolvePermissionIds(db, request.PermissionCodes, ct);
                if (resolved.Error is not null)
                    return ApiResults.Fail(resolved.Error);

                var role = new Role { Code = request.Code, Name = request.Name.Trim(), IsActive = request.IsActive };
                role.Permissions.AddRange(resolved.Ids.Select(id => new RolePermission { PermissionId = id }));
                db.Roles.Add(role);
                await db.SaveChangesAsync(ct);
                generations.Invalidate(tenant.RequiredTenantId);
                return ApiResults.Created($"/api/iam/roles/{role.Id}", ToDto(role, await PermissionCodesById(db, ct)));
            })
            .Validate<SaveRoleRequest>()
            .RequirePermission(IamPermissions.RolesWrite)
            .WithName("CreateRole");

        roles.MapPut("/{id:int}", async (int id, SaveRoleRequest request, IamDbContext db, ITenantContext tenant,
                PermissionGenerations generations, DelegationGuard guard, CancellationToken ct) =>
            {
                var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, ct);
                if (role is null)
                    return ApiResults.NotFound("The role");
                if (await guard.CheckRoleChangeAsync(role, role.Code == Role.OwnerCode ? [] : request.PermissionCodes, ct) is { } denied)
                    return ApiResults.Fail(denied, StatusCodes.Status403Forbidden);
                if (role.IsSystem && request.Code != role.Code)
                    return ApiResults.Fail("The code of a built-in role cannot change.", StatusCodes.Status422UnprocessableEntity);
                if (role.Code == Role.OwnerCode && !request.IsActive)
                    return ApiResults.Fail("The Company Admin role cannot be deactivated.", StatusCodes.Status422UnprocessableEntity);
                if (!role.IsSystem && await db.Roles.AnyAsync(r => r.Code == request.Code && r.Id != id, ct))
                    return ApiResults.Fail("A role with this code already exists.", StatusCodes.Status409Conflict);
                if (!RowVersions.TryApply(db, role, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");

                role.Code = request.Code;
                role.Name = request.Name.Trim();
                role.IsActive = request.IsActive;

                // The administrator role always holds every permission; its grants are not editable.
                if (role.Code != Role.OwnerCode)
                {
                    var resolved = await ResolvePermissionIds(db, request.PermissionCodes, ct);
                    if (resolved.Error is not null)
                        return ApiResults.Fail(resolved.Error);
                    db.RolePermissions.RemoveRange(role.Permissions.Where(p => !resolved.Ids.Contains(p.PermissionId)));
                    var existing = role.Permissions.Select(p => p.PermissionId).ToHashSet();
                    role.Permissions.AddRange(resolved.Ids.Where(pid => !existing.Contains(pid))
                        .Select(pid => new RolePermission { PermissionId = pid }));
                }

                await db.SaveChangesAsync(ct);
                generations.Invalidate(tenant.RequiredTenantId);
                return ApiResults.Ok(ToDto(role, await PermissionCodesById(db, ct)));
            })
            .Validate<SaveRoleRequest>()
            .RequirePermission(IamPermissions.RolesWrite)
            .WithName("UpdateRole");

        roles.MapDelete("/{id:int}", async (int id, IamDbContext db, ITenantContext tenant,
                PermissionGenerations generations, DelegationGuard guard, CancellationToken ct) =>
            {
                var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, ct);
                if (role is null)
                    return ApiResults.NotFound("The role");
                if (await guard.CheckRoleChangeAsync(role, [], ct) is { } denied)
                    return ApiResults.Fail(denied, StatusCodes.Status403Forbidden);
                if (role.IsSystem)
                    return ApiResults.Fail("Built-in roles cannot be deleted.", StatusCodes.Status422UnprocessableEntity);
                if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id, ct))
                    return ApiResults.Fail("Remove this role from its staff before deleting it.", StatusCodes.Status422UnprocessableEntity);

                db.RolePermissions.RemoveRange(role.Permissions);
                db.Roles.Remove(role);
                await db.SaveChangesAsync(ct);
                generations.Invalidate(tenant.RequiredTenantId);
                return ApiResults.Ok("Role deleted.");
            })
            .RequirePermission(IamPermissions.RolesWrite)
            .WithName("DeleteRole");
    }

    private static async Task<Dictionary<int, string>> PermissionCodesById(IamDbContext db, CancellationToken ct)
        => await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, ct);

    private static async Task<(HashSet<int> Ids, string? Error)> ResolvePermissionIds(IamDbContext db,
        IReadOnlyList<string> codes, CancellationToken ct)
    {
        var wanted = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
        var found = await db.Permissions.AsNoTracking().Where(p => p.IsActive && p.Module != "owner" && p.Module != "group" && wanted.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var unknown = wanted.Where(c => !found.ContainsKey(c)).ToList();
        return unknown.Count > 0
            ? ([], $"Unknown permission: {string.Join(", ", unknown)}.")
            : (found.Values.ToHashSet(), null);
    }

    private static RoleDto ToDto(Role role, IReadOnlyDictionary<int, string> codes)
        => new(role.Id, role.Code, role.Name, role.IsSystem, role.IsActive,
            role.Permissions.Where(p => p.IsActive && codes.ContainsKey(p.PermissionId))
                .Select(p => codes[p.PermissionId]).Order(StringComparer.Ordinal).ToList(),
            role.ToBase64());
}
