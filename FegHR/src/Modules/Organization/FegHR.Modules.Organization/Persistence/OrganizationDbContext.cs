using System.Data;
using Dapper;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Organization.Domain;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Organization.Persistence;

/// <summary>Maps the <c>org</c> schema only. Tables are created by the versioned SQL scripts.</summary>
internal sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "org";

    protected override string Schema => SchemaName;

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
}

/// <summary>Stored procedures of the <c>org</c> schema.</summary>
internal sealed class OrganizationProcedures(ITenantDbConnectionFactory connections)
{
    /// <summary>The next free employee code of the bound company (EMP0001, EMP0002, ...).</summary>
    public async Task<string> NextEmployeeCodeAsync(CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string>(new CommandDefinition("org.usp_Employee_NextCode",
            commandType: CommandType.StoredProcedure, cancellationToken: ct)) ?? "EMP0001";
    }
}

internal sealed class EmployeeDirectory(OrganizationDbContext db) : IEmployeeDirectory
{
    public Task<EmployeeRef?> GetByUserAsync(int userId, CancellationToken ct = default)
        => Refs(db.Employees.Where(e => e.UserId == userId && e.IsActive)).FirstOrDefaultAsync(ct);

    public Task<EmployeeRef?> GetAsync(int employeeId, CancellationToken ct = default)
        => Refs(db.Employees.Where(e => e.Id == employeeId)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<int, EmployeeRef>> GetManyAsync(IReadOnlyCollection<int> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<int, EmployeeRef>();
        var ids = employeeIds.Distinct().ToList();
        return await Refs(db.Employees.Where(e => ids.Contains(e.Id))).ToDictionaryAsync(e => e.Id, ct);
    }

    public async Task<IReadOnlyList<int>> GetDirectReportIdsAsync(int managerEmployeeId, CancellationToken ct = default)
        => await db.Employees.AsNoTracking().Where(e => e.ReportsToId == managerEmployeeId && e.IsActive)
            .Select(e => e.Id).ToListAsync(ct);

    private static IQueryable<EmployeeRef> Refs(IQueryable<Employee> query)
        => query.AsNoTracking().Select(e => new EmployeeRef(e.Id, e.Code, e.FullName, e.UserId, e.ReportsToId, e.BranchId,
            e.DepartmentId, e.IsActive));
}
