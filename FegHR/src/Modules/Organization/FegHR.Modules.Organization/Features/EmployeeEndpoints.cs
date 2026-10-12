using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Iam.Contracts;
using FegHR.Modules.Organization.Domain;
using FegHR.Modules.Organization.Persistence;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Organization.Features;

internal sealed record EmployeeDto(
    int Id, string Code, string FullName, string? Gender, DateOnly? DateOfBirth, string? Phone, string? Email,
    DateOnly? JoiningDate, DateOnly? ExitDate, string? EmploymentType,
    int? BranchId, string? Branch, int? DepartmentId, string? Department, int? DesignationId, string? Designation,
    int? ReportsToId, string? ReportsTo, int? UserId, string? UserLogin, bool IsActive, string RowVersion);

internal sealed record SaveEmployeeRequest(
    [property: StringLength(50)] string? Code,
    [property: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [property: StringLength(20)] string? Gender,
    DateOnly? DateOfBirth,
    [property: StringLength(30)] string? Phone,
    [property: EmailAddress, StringLength(254)] string? Email,
    DateOnly? JoiningDate,
    DateOnly? ExitDate,
    [property: StringLength(50)] string? EmploymentType,
    int? BranchId,
    int? DepartmentId,
    int? DesignationId,
    int? ReportsToId,
    int? UserId,
    bool IsActive = true,
    string? RowVersion = null);

internal sealed record EmployeeQuery(int Page = 1, int PageSize = 20, string? Search = null, int? DepartmentId = null,
    int? BranchId = null, bool? IsActive = null);

internal static class EmployeeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var employees = group.MapGroup("/employees");

        employees.MapGet("/", async ([AsParameters] EmployeeQuery query, OrganizationDbContext db, ITenantDirectory tenants,
                IMemberDirectory members, CancellationToken ct) =>
            {
                var page = new PageRequest(query.Page, query.PageSize, query.Search);
                var rows = db.Employees.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var term = query.Search.Trim();
                    rows = rows.Where(e => e.FullName.Contains(term) || e.Code.Contains(term) || (e.Phone != null && e.Phone.Contains(term)));
                }
                if (query.DepartmentId is { } departmentId) rows = rows.Where(e => e.DepartmentId == departmentId);
                if (query.BranchId is { } branchId) rows = rows.Where(e => e.BranchId == branchId);
                if (query.IsActive is { } active) rows = rows.Where(e => e.IsActive == active);

                var total = await rows.CountAsync(ct);
                var items = await rows.OrderBy(e => e.Code).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
                return ApiResults.Ok(new PagedResult<EmployeeDto>(await ToDtosAsync(items, db, tenants, members, ct), total,
                    page.SafePage, page.SafePageSize));
            })
            .RequirePermission(OrganizationPermissions.EmployeesRead)
            .WithName("ListEmployees");

        employees.MapGet("/{id:int}", async (int id, OrganizationDbContext db, ITenantDirectory tenants, IMemberDirectory members,
                CancellationToken ct) =>
                await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) is { } row
                    ? ApiResults.Ok((await ToDtosAsync([row], db, tenants, members, ct))[0])
                    : ApiResults.NotFound("The employee"))
            .RequirePermission(OrganizationPermissions.EmployeesRead)
            .WithName("GetEmployee");

        // The signed-in person's own employee record, for the self-service screens.
        employees.MapGet("/me", async (ITenantContext tenant, OrganizationDbContext db, ITenantDirectory tenants,
                IMemberDirectory members, CancellationToken ct) =>
                await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == tenant.UserId && e.IsActive, ct) is { } row
                    ? ApiResults.Ok((await ToDtosAsync([row], db, tenants, members, ct))[0])
                    : ApiResults.NotFound("Your employee record"))
            .RequireStaff()
            .WithName("GetMyEmployee");

        // Sign-ins that are not linked to an employee yet, for the "user account" picker.
        employees.MapGet("/unlinked-users", async (OrganizationDbContext db, IMemberDirectory members, CancellationToken ct) =>
            {
                var linked = (await db.Employees.AsNoTracking().Where(e => e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct)).ToHashSet();
                return ApiResults.Ok((await members.ListAsync(ct)).Where(m => m.IsActive && !linked.Contains(m.UserId))
                    .Select(m => new LookupItem(m.UserId, $"{m.DisplayName} ({m.Email ?? m.Phone})")).ToList());
            })
            .RequirePermission(OrganizationPermissions.EmployeesWrite)
            .WithName("ListUnlinkedUsers");

        employees.MapPost("/", async (SaveEmployeeRequest request, OrganizationDbContext db, OrganizationProcedures procedures,
                ITenantDirectory tenants, IMemberDirectory members, CancellationToken ct) =>
            {
                var row = new Employee();
                var code = string.IsNullOrWhiteSpace(request.Code) ? await procedures.NextEmployeeCodeAsync(ct) : request.Code.Trim();
                if (await ProblemAsync(request, code, null, db, tenants, members, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                Apply(row, request, code);
                db.Employees.Add(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/org/employees/{row.Id}", (await ToDtosAsync([row], db, tenants, members, ct))[0],
                    "Employee added.");
            })
            .Validate<SaveEmployeeRequest>()
            .RequirePermission(OrganizationPermissions.EmployeesWrite)
            .WithName("CreateEmployee");

        employees.MapPut("/{id:int}", async (int id, SaveEmployeeRequest request, OrganizationDbContext db, ITenantDirectory tenants,
                IMemberDirectory members, CancellationToken ct) =>
            {
                var row = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The employee");
                var code = string.IsNullOrWhiteSpace(request.Code) ? row.Code : request.Code.Trim();
                if (await ProblemAsync(request, code, id, db, tenants, members, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                Apply(row, request, code);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok((await ToDtosAsync([row], db, tenants, members, ct))[0], "Employee saved.");
            })
            .Validate<SaveEmployeeRequest>()
            .RequirePermission(OrganizationPermissions.EmployeesWrite)
            .WithName("UpdateEmployee");
    }

    /// <summary>Null when the request is consistent; otherwise the message to show.</summary>
    private static async Task<string?> ProblemAsync(SaveEmployeeRequest r, string code, int? id, OrganizationDbContext db,
        ITenantDirectory tenants, IMemberDirectory members, CancellationToken ct)
    {
        if (await db.Employees.AnyAsync(e => e.Code == code && e.Id != id, ct))
            return "Another employee already has this code.";
        if (r.ExitDate is { } exit && r.JoiningDate is { } joined && exit < joined)
            return "The exit date cannot be before the joining date.";
        if (r.DepartmentId is { } departmentId && !await db.Departments.AnyAsync(d => d.Id == departmentId, ct))
            return "The department does not exist.";
        if (r.DesignationId is { } designationId && !await db.Designations.AnyAsync(d => d.Id == designationId, ct))
            return "The designation does not exist.";
        if (r.BranchId is { } branchId && (await tenants.GetBranchesAsync(activeOnly: false, ct)).All(b => b.Id != branchId))
            return "The branch does not exist.";
        if (r.ReportsToId is { } managerId)
        {
            if (managerId == id)
                return "An employee cannot report to themselves.";
            if (!await db.Employees.AnyAsync(e => e.Id == managerId, ct))
                return "The line manager does not exist.";
            // Walk up from the proposed manager: reaching this employee would make a loop.
            var seen = new HashSet<int>();
            for (int? current = managerId; current is { } next && id is not null && seen.Add(next);)
            {
                current = await db.Employees.Where(e => e.Id == next).Select(e => e.ReportsToId).FirstOrDefaultAsync(ct);
                if (current == id)
                    return "This reporting line would loop back to the same employee.";
            }
        }
        if (r.UserId is { } userId)
        {
            if (await members.FindAsync(userId, ct) is null)
                return "The user account is not a member of this company.";
            if (await db.Employees.AnyAsync(e => e.UserId == userId && e.Id != id, ct))
                return "That user account is already linked to another employee.";
        }
        return null;
    }

    private static void Apply(Employee row, SaveEmployeeRequest r, string code)
    {
        row.Code = code;
        row.FullName = r.FullName.Trim();
        row.Gender = Clean(r.Gender);
        row.DateOfBirth = r.DateOfBirth;
        row.Phone = Clean(r.Phone);
        row.Email = Clean(r.Email);
        row.JoiningDate = r.JoiningDate;
        row.ExitDate = r.ExitDate;
        row.EmploymentType = Clean(r.EmploymentType);
        row.BranchId = r.BranchId;
        row.DepartmentId = r.DepartmentId;
        row.DesignationId = r.DesignationId;
        row.ReportsToId = r.ReportsToId;
        row.UserId = r.UserId;
        row.IsActive = r.IsActive;
    }

    private static async Task<List<EmployeeDto>> ToDtosAsync(IReadOnlyList<Employee> rows, OrganizationDbContext db,
        ITenantDirectory tenants, IMemberDirectory members, CancellationToken ct)
    {
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var designations = await db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var branches = (await tenants.GetBranchesAsync(activeOnly: false, ct)).ToDictionary(b => b.Id, b => b.Name);
        var managerIds = rows.Where(r => r.ReportsToId is not null).Select(r => r.ReportsToId!.Value).Distinct().ToList();
        var managers = managerIds.Count == 0 ? []
            : await db.Employees.AsNoTracking().Where(e => managerIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var logins = rows.Any(r => r.UserId is not null)
            ? (await members.ListAsync(ct)).ToDictionary(m => m.UserId, m => m.Email ?? m.Phone)
            : [];

        return rows.Select(e => new EmployeeDto(e.Id, e.Code, e.FullName, e.Gender, e.DateOfBirth, e.Phone, e.Email,
            e.JoiningDate, e.ExitDate, e.EmploymentType,
            e.BranchId, Name(branches, e.BranchId), e.DepartmentId, Name(departments, e.DepartmentId),
            e.DesignationId, Name(designations, e.DesignationId), e.ReportsToId, Name(managers, e.ReportsToId),
            e.UserId, e.UserId is { } u ? logins.GetValueOrDefault(u) : null, e.IsActive, Convert.ToBase64String(e.RowVersion))).ToList();
    }

    private static string? Name(Dictionary<int, string> names, int? id) => id is { } key ? names.GetValueOrDefault(key) : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
