using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Platform.Contracts;
using FegHR.Modules.Platform.Domain;
using FegHR.Modules.Platform.Persistence;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Platform.Features.Branches;

internal sealed record BranchDto(int Id, string Code, string Name, string? Address, string? Phone, bool IsActive, string RowVersion);

internal sealed record SaveBranchRequest(
    [property: Required, StringLength(40, MinimumLength = 1), RegularExpression("^[A-Za-z0-9-]+$",
        ErrorMessage = "Use letters, digits and dashes.")] string Code,
    [property: Required, StringLength(150, MinimumLength = 1)] string Name,
    [property: StringLength(500)] string? Address,
    [property: StringLength(30)] string? Phone,
    bool IsActive = true,
    string? RowVersion = null);

internal static class BranchEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var branches = group.MapGroup("/branches");

        branches.MapGet("/", async (PlatformDbContext db, CancellationToken ct) =>
                ApiResults.Ok(await db.Branches.AsNoTracking().OrderBy(o => o.Name).Select(o => ToDto(o)).ToListAsync(ct)))
            .RequirePermission(PlatformPermissions.BranchesRead)
            .WithName("ListBranches");

        // Branches for pickers. Iam narrows the list further per user.
        branches.MapGet("/active", async (ITenantDirectory tenants, CancellationToken ct) =>
                ApiResults.Ok(await tenants.GetBranchesAsync(activeOnly: true, ct)))
            .RequireStaff()
            .WithName("ListActiveBranches");

        branches.MapPost("/", async (SaveBranchRequest request, PlatformDbContext db, CancellationToken ct) =>
            {
                if (await db.Branches.AnyAsync(o => o.Code == request.Code, ct))
                    return ApiResults.Fail("A branch with this code already exists.", StatusCodes.Status409Conflict);
                var branch = new Branch();
                Apply(branch, request);
                db.Branches.Add(branch);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/platform/branches/{branch.Id}", ToDto(branch));
            })
            .Validate<SaveBranchRequest>()
            .RequirePermission(PlatformPermissions.BranchesWrite)
            .WithName("CreateBranch");

        branches.MapPut("/{id:int}", async (int id, SaveBranchRequest request, PlatformDbContext db, CancellationToken ct) =>
            {
                var branch = await db.Branches.SingleOrDefaultAsync(o => o.Id == id, ct);
                if (branch is null)
                    return ApiResults.NotFound("The branch");
                if (await db.Branches.AnyAsync(o => o.Code == request.Code && o.Id != id, ct))
                    return ApiResults.Fail("A branch with this code already exists.", StatusCodes.Status409Conflict);
                if (!RowVersions.TryApply(db, branch, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                Apply(branch, request);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(branch));
            })
            .Validate<SaveBranchRequest>()
            .RequirePermission(PlatformPermissions.BranchesWrite)
            .WithName("UpdateBranch");
    }

    private static void Apply(Branch branch, SaveBranchRequest request)
    {
        branch.Code = request.Code.Trim();
        branch.Name = request.Name.Trim();
        branch.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        branch.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        branch.IsActive = request.IsActive;
    }

    private static BranchDto ToDto(Branch o)
        => new(o.Id, o.Code, o.Name, o.Address, o.Phone, o.IsActive, Convert.ToBase64String(o.RowVersion));
}
