using System.Data;
using Dapper;
using FegHR.Modules.Attendance.Domain;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Attendance.Persistence;

/// <summary>Maps the <c>attendance</c> schema only. Tables are created by the versioned SQL scripts.</summary>
internal sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "attendance";

    protected override string Schema => SchemaName;

    public DbSet<AttendanceSetting> Settings => Set<AttendanceSetting>();
    public DbSet<WorkLocation> WorkLocations => Set<WorkLocation>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<WorkScheduleDay> WorkScheduleDays => Set<WorkScheduleDay>();
    public DbSet<EmployeeSchedule> EmployeeSchedules => Set<EmployeeSchedule>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<EmployeeDevice> EmployeeDevices => Set<EmployeeDevice>();
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AttendanceDbContext).Assembly);
}

/// <summary>
/// Stored procedures the attendance module calls: the punch itself (direction, geofence verdict
/// and day row are decided in SQL, in one transaction) and the cross-schema reports of the
/// <c>reporting</c> schema.
/// </summary>
internal sealed class AttendanceProcedures(ITenantDbConnectionFactory connections)
{
    public async Task<PunchResult> RecordPunchAsync(PunchCommand c, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleAsync<PunchResult>(new CommandDefinition("attendance.usp_Punch_Record", new
        {
            c.EmployeeId, c.Latitude, c.Longitude, c.AccuracyMeters, c.IsMockLocation, c.DeviceId, c.DeviceLabel, c.Platform,
            c.ClientPunchedAt, c.SelfieKey, c.Remarks, c.IpAddress, c.Now,
            LogDate = c.LogDate.ToDateTime(TimeOnly.MinValue), LocalTime = c.LocalTime.ToTimeSpan(), c.ActorId
        }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task ReconcileAsync(int employeeId, DateOnly logDate, string source, DateTimeOffset now, int actorId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("attendance.usp_DailyLog_Reconcile", new
        {
            EmployeeId = employeeId, LogDate = logDate.ToDateTime(TimeOnly.MinValue), Source = source, Now = now, ActorId = actorId
        }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    /// <summary>The employee's shift on a date; null when the company has no schedule configured.</summary>
    public async Task<ScheduleForDate?> ScheduleForDateAsync(int employeeId, DateOnly date, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<ScheduleForDate>(new CommandDefinition("attendance.usp_Schedule_ForDate",
            new { EmployeeId = employeeId, Date = D(date) }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public Task<IReadOnlyList<DailyAttendanceRow>> DailyAsync(DateOnly date, DateOnly today, int? branchId, int? departmentId, CancellationToken ct)
        => ListAsync<DailyAttendanceRow>("reporting.usp_Attendance_Daily",
            new { Date = D(date), Today = D(today), BranchId = branchId, DepartmentId = departmentId }, ct);

    public Task<IReadOnlyList<MonthlyAttendanceRow>> MonthlyAsync(DateOnly from, DateOnly to, DateOnly today, int? branchId,
        int? departmentId, CancellationToken ct)
        => ListAsync<MonthlyAttendanceRow>("reporting.usp_Attendance_Monthly",
            new { From = D(from), To = D(to), Today = D(today), BranchId = branchId, DepartmentId = departmentId }, ct);

    public Task<IReadOnlyList<EmployeeDayRow>> EmployeeDaysAsync(int employeeId, DateOnly from, DateOnly to, DateOnly today, CancellationToken ct)
        => ListAsync<EmployeeDayRow>("reporting.usp_Attendance_Employee",
            new { EmployeeId = employeeId, From = D(from), To = D(to), Today = D(today) }, ct);

    private async Task<IReadOnlyList<T>> ListAsync<T>(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }

    private static DateTime D(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}

internal sealed record PunchCommand(
    int EmployeeId, decimal? Latitude, decimal? Longitude, double? AccuracyMeters, bool? IsMockLocation,
    string? DeviceId, string? DeviceLabel, string? Platform, DateTimeOffset? ClientPunchedAt, string? SelfieKey,
    string? Remarks, string? IpAddress, DateTimeOffset Now, DateOnly LogDate, TimeOnly LocalTime, int ActorId);

internal sealed class ScheduleForDate
{
    public bool IsWorkingDay { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
}

internal sealed class PunchResult
{
    public long Id { get; set; }
    public DateTimeOffset PunchedAt { get; set; }
    public DateTime LogDate { get; set; }
    public TimeSpan LocalTime { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string LocationStatus { get; set; } = string.Empty;
    public double? DistanceMeters { get; set; }
    public string FlagsJson { get; set; } = "[]";
    public string? SelfieKey { get; set; }
    public string? LocationName { get; set; }
    public bool WasDuplicate { get; set; }
}

internal sealed class DailyAttendanceRow
{
    public int EmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? Branch { get; set; }
    public DateTime LogDate { get; set; }
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public TimeSpan? ShiftStart { get; set; }
    public TimeSpan? ShiftEnd { get; set; }
    public int? WorkedMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Source { get; set; }
}

internal sealed class MonthlyAttendanceRow
{
    public int EmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public int PresentDays { get; set; }
    public int LateDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int WeekOffDays { get; set; }
    public int HolidayDays { get; set; }
    public int WorkedMinutes { get; set; }
}

internal sealed class EmployeeDayRow
{
    public DateTime LogDate { get; set; }
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public TimeSpan? ShiftStart { get; set; }
    public TimeSpan? ShiftEnd { get; set; }
    public int? WorkedMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Source { get; set; }
}
