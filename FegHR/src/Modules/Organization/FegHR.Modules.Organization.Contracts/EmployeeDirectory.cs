namespace FegHR.Modules.Organization.Contracts;

/// <summary>An employee of the bound company, as the attendance and leave modules see one.</summary>
public sealed record EmployeeRef(
    int Id,
    string Code,
    string FullName,
    int? UserId,
    int? ReportsToId,
    int? BranchId,
    int? DepartmentId,
    bool IsActive);

/// <summary>
/// Read access to the bound company's employees. Attendance and leave keep only an employee id;
/// names and reporting lines come from here, never from a join into <c>org</c> tables.
/// </summary>
public interface IEmployeeDirectory
{
    /// <summary>The employee record linked to a sign-in: what makes self-service "my own" and nobody else's.</summary>
    Task<EmployeeRef?> GetByUserAsync(int userId, CancellationToken ct = default);

    Task<EmployeeRef?> GetAsync(int employeeId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, EmployeeRef>> GetManyAsync(IReadOnlyCollection<int> employeeIds, CancellationToken ct = default);

    /// <summary>Employees who report directly to <paramref name="managerEmployeeId"/>.</summary>
    Task<IReadOnlyList<int>> GetDirectReportIdsAsync(int managerEmployeeId, CancellationToken ct = default);
}
