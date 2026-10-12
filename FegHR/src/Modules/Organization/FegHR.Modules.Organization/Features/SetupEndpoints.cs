using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Organization.Domain;
using FegHR.Modules.Organization.Persistence;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Organization.Features;

/// <summary>Permission codes seeded by <c>SeedData/01_Metadata.sql</c>.</summary>
internal static class OrganizationPermissions
{
    public const string EmployeesRead = "org.employees.read";
    public const string EmployeesWrite = "org.employees.write";
    public const string SetupRead = "org.setup.read";
    public const string SetupWrite = "org.setup.write";
}

internal sealed record NamedDto(int Id, string Name, bool IsActive, string RowVersion);

internal sealed record SaveNamedRequest(
    [property: Required, StringLength(150, MinimumLength = 1)] string Name,
    bool IsActive = true,
    string? RowVersion = null);

internal sealed record LookupItem(int Id, string Name);

/// <summary>Everything the pickers of the attendance, leave and employee screens need, in one call.</summary>
internal sealed record Lookups(
    IReadOnlyList<LookupItem> Departments,
    IReadOnlyList<LookupItem> Designations,
    IReadOnlyList<LookupItem> Branches,
    IReadOnlyList<LookupItem> Employees);

/// <summary>Departments and designations: the same simple CRUD over two tables.</summary>
internal static class SetupEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        MapNamed(group, "departments", "Department", db => db.Departments, (db, id) => db.Employees.AnyAsync(e => e.DepartmentId == id));
        MapNamed(group, "designations", "Designation", db => db.Designations, (db, id) => db.Employees.AnyAsync(e => e.DesignationId == id));

        group.MapGet("/lookups", async (OrganizationDbContext db, ITenantDirectory tenants, CancellationToken ct) =>
                ApiResults.Ok(new Lookups(
                    await db.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct),
                    await db.Designations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct),
                    (await tenants.GetBranchesAsync(activeOnly: true, ct)).Select(b => new LookupItem(b.Id, b.Name)).ToList(),
                    await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName)
                        .Select(x => new LookupItem(x.Id, x.Code + " - " + x.FullName)).ToListAsync(ct))))
            .RequireStaff()
            .WithName("GetOrganizationLookups");
    }

    private static void MapNamed<TEntity>(RouteGroupBuilder group, string route, string label,
        Func<OrganizationDbContext, DbSet<TEntity>> set, Func<OrganizationDbContext, int, Task<bool>> inUse)
        where TEntity : NamedEntity, new()
    {
        var rows = group.MapGroup("/" + route);

        rows.MapGet("/", async (OrganizationDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await set(db).AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)).Select(ToDto).ToList()))
            .RequirePermission(OrganizationPermissions.SetupRead)
            .WithName($"List{label}s");

        rows.MapPost("/", async (SaveNamedRequest request, OrganizationDbContext db, CancellationToken ct) =>
            {
                var name = request.Name.Trim();
                if (await set(db).AnyAsync(x => x.Name == name, ct))
                    return ApiResults.Fail($"A {label.ToLowerInvariant()} with this name already exists.", StatusCodes.Status409Conflict);
                var row = new TEntity { Name = name, IsActive = request.IsActive };
                set(db).Add(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/org/{route}/{row.Id}", ToDto(row), $"{label} added.");
            })
            .Validate<SaveNamedRequest>()
            .RequirePermission(OrganizationPermissions.SetupWrite)
            .WithName($"Create{label}");

        rows.MapPut("/{id:int}", async (int id, SaveNamedRequest request, OrganizationDbContext db, CancellationToken ct) =>
            {
                var row = await set(db).SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound($"The {label.ToLowerInvariant()}");
                var name = request.Name.Trim();
                if (await set(db).AnyAsync(x => x.Name == name && x.Id != id, ct))
                    return ApiResults.Fail($"A {label.ToLowerInvariant()} with this name already exists.", StatusCodes.Status409Conflict);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                row.Name = name;
                row.IsActive = request.IsActive;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), $"{label} saved.");
            })
            .Validate<SaveNamedRequest>()
            .RequirePermission(OrganizationPermissions.SetupWrite)
            .WithName($"Update{label}");

        rows.MapDelete("/{id:int}", async (int id, OrganizationDbContext db, CancellationToken ct) =>
            {
                var row = await set(db).SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound($"The {label.ToLowerInvariant()}");
                if (await inUse(db, id))
                    return ApiResults.Fail($"This {label.ToLowerInvariant()} is used by employees. Deactivate it instead.",
                        StatusCodes.Status422UnprocessableEntity);
                set(db).Remove(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok($"{label} deleted.");
            })
            .RequirePermission(OrganizationPermissions.SetupWrite)
            .WithName($"Delete{label}");
    }

    private static NamedDto ToDto(NamedEntity row) => new(row.Id, row.Name, row.IsActive, Convert.ToBase64String(row.RowVersion));
}
