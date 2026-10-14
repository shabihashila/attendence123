using System.Data;
using Dapper;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FegHR.Modules.Leave.Persistence;

/// <summary><c>leave.LeaveTypes</c>. A null <see cref="YearlyQuota"/> means the type has no limit.</summary>
internal sealed class LeaveType : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? YearlyQuota { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// <c>leave.Applications</c>. Read-only here: applications are written by the
/// <c>leave.usp_Application_*</c> procedures, which hold the balance and overlap rules.
/// </summary>
internal sealed class LeaveApplication : TenantEntity
{
    public int EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public string DayType { get; set; } = "Full";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Days { get; set; }
    public DateOnly AppliedOn { get; set; }
    public string? Reason { get; set; }
    public string? AddressDuringLeave { get; set; }
    public string Status { get; set; } = "Pending";
    public string CurrentStep { get; set; } = "Manager";
    public bool UsesApprovalWorkflow { get; set; }
    public int? ApproverUserId { get; set; }
    public int? Level1By { get; set; }
    public DateTimeOffset? Level1At { get; set; }
    public string? Level1Remarks { get; set; }
    public int? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionRemarks { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>leave.Adjustments</c>: a yearly correction to one employee's balance of one type.</summary>
internal sealed class LeaveAdjustment : TenantEntity
{
    public int EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
}

internal sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable("LeaveTypes");
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.YearlyQuota).HasColumnType("decimal(5,1)");
    }
}

internal sealed class LeaveApplicationConfiguration : IEntityTypeConfiguration<LeaveApplication>
{
    public void Configure(EntityTypeBuilder<LeaveApplication> b)
    {
        b.ToTable("Applications");
        b.Property(x => x.Days).HasColumnType("decimal(5,1)");
    }
}

internal sealed class LeaveAdjustmentConfiguration : IEntityTypeConfiguration<LeaveAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveAdjustment> b)
    {
        b.ToTable("Adjustments");
        b.Property(x => x.Days).HasColumnType("decimal(5,1)");
        b.Property(x => x.Reason).HasMaxLength(500);
    }
}

/// <summary>Maps the <c>leave</c> schema only. Tables are created by the versioned SQL scripts.</summary>
internal sealed class LeaveDbContext(DbContextOptions<LeaveDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "leave";

    protected override string Schema => SchemaName;

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveApplication> Applications => Set<LeaveApplication>();
    public DbSet<LeaveAdjustment> Adjustments => Set<LeaveAdjustment>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(LeaveDbContext).Assembly);
}

/// <summary>
/// The leave procedures. Applying and deciding run in SQL under a per-employee lock, so two
/// requests cannot both spend the same balance; the statement is a cross-schema report.
/// </summary>
internal sealed class LeaveProcedures(ITenantDbConnectionFactory connections)
{
    public async Task ReassignAsync(int applicationId,int newUserId,string reason,int expectedApproverId,CancellationToken ct)
    {
        await using var c=await connections.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("leave.usp_Approval_Reassign",new {ApplicationId=applicationId,NewUserId=newUserId,Reason=reason,ExpectedApproverId=expectedApproverId},commandType:CommandType.StoredProcedure,cancellationToken:ct));
    }
    public async Task<IReadOnlyList<ApprovalPolicyRow>> PolicyAsync(int? levels, int? finalApproverUserId, bool isEnabled, byte[]? rowVersion, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct);
        var p = new DynamicParameters(new { Levels = levels, FinalApproverUserId = finalApproverUserId, IsEnabled = isEnabled });
        p.Add("RowVersion", rowVersion, DbType.Binary, size: 8);
        return (await c.QueryAsync<ApprovalPolicyRow>(new CommandDefinition("leave.usp_ApprovalPolicy", p, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<ApprovalHistoryRow>> HistoryAsync(int applicationId, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct);
        return (await c.QueryAsync<ApprovalHistoryRow>(new CommandDefinition("leave.usp_ApprovalHistory", new { ApplicationId = applicationId }, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }

    public async Task<DecidedApplication> WorkflowDecideAsync(int id, bool approve, string? remarks, DateTimeOffset now, int actorId, CancellationToken ct)
    {
        await using var c = await connections.OpenAsync(ct);
        return await c.QuerySingleAsync<DecidedApplication>(new CommandDefinition("leave.usp_Workflow_Decide", new { ApplicationId = id, Approve = approve, Remarks = remarks, Now = now, ActorId = actorId }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
    public Task<IReadOnlyList<BalanceRow>> BalanceAsync(int employeeId, int year, CancellationToken ct)
        => ListAsync<BalanceRow>("leave.usp_Balance_Get", new { EmployeeId = employeeId, Year = year }, ct);

    public async Task<SubmittedApplication> SubmitAsync(int employeeId, int leaveTypeId, string dayType, DateOnly start, DateOnly end,
        string? reason, string? address, bool startAtHr, DateOnly today, DateTimeOffset now, int actorId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleAsync<SubmittedApplication>(new CommandDefinition("leave.usp_Application_Submit", new
        {
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, DayType = dayType, StartDate = D(start), EndDate = D(end),
            Reason = reason, AddressDuringLeave = address, StartAtHr = startAtHr, Today = D(today), Now = now, ActorId = actorId
        }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<DecidedApplication> DecideAsync(int applicationId, bool approve, bool canLevel1, bool canLevel2,
        int? actorEmployeeId, string? remarks, DateTimeOffset now, int actorId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleAsync<DecidedApplication>(new CommandDefinition("leave.usp_Application_Decide", new
        {
            ApplicationId = applicationId, Approve = approve, CanLevel1 = canLevel1, CanLevel2 = canLevel2,
            ActorEmployeeId = actorEmployeeId, Remarks = remarks, Now = now, ActorId = actorId
        }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task CancelAsync(int applicationId, int employeeId, DateTimeOffset now, int actorId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("leave.usp_Application_Cancel",
            new { ApplicationId = applicationId, EmployeeId = employeeId, Now = now, ActorId = actorId },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public Task<IReadOnlyList<StatementRow>> StatementAsync(int year, int? departmentId, int? employeeId, CancellationToken ct)
        => ListAsync<StatementRow>("reporting.usp_Leave_Statement", new { Year = year, DepartmentId = departmentId, EmployeeId = employeeId }, ct);

    private async Task<IReadOnlyList<T>> ListAsync<T>(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }

    private static DateTime D(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}

internal sealed class BalanceRow
{
    public int LeaveTypeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public decimal? Quota { get; set; }
    public decimal Adjustment { get; set; }
    public decimal Used { get; set; }
    public decimal Pending { get; set; }
    public decimal? Remaining { get; set; }
}

internal sealed record ApprovalPolicyRow(int SupervisorLevels, int? FinalApproverUserId, bool IsEnabled, byte[] RowVersion);
internal sealed record ApprovalHistoryRow(int Stage, string Approver, string Status, DateTimeOffset? DecidedAt, string? Remarks);

internal sealed class StatementRow
{
    public int EmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public int LeaveTypeId { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public decimal? Quota { get; set; }
    public decimal Adjustment { get; set; }
    public decimal Used { get; set; }
    public decimal Pending { get; set; }
    public decimal? Remaining { get; set; }
}

internal sealed class SubmittedApplication
{
    public int Id { get; set; }
    public decimal Days { get; set; }
}

internal sealed class DecidedApplication
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
}
