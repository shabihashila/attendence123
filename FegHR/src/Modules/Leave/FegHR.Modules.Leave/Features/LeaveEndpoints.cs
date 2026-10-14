using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Leave.Persistence;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Shared.Time;

namespace FegHR.Modules.Leave.Features;

/// <summary>Permission codes seeded by <c>SeedData/01_Metadata.sql</c>.</summary>
internal static class LeavePermissions
{
    public const string ApplySelf = "leave.apply.self";
    public const string ApplicationsRead = "leave.applications.read";
    public const string ApproveLevel1 = "leave.approve.l1";
    public const string ApproveLevel2 = "leave.approve.l2";
    public const string ApproveLevel3 = "leave.approve.l3";
    public const string ApproveLevel4 = "leave.approve.l4";
    public const string ApproveLevel5 = "leave.approve.l5";
    public const string ApproveFinal = "leave.approve.final";
    public const string Adjust = "leave.adjust";
    public const string SetupWrite = "leave.setup.write";
    public const string ReportsRead = "leave.reports.read";
}

internal sealed record LeaveTypeDto(int Id, string Code, string Name, decimal? YearlyQuota, bool IsPaid, bool IsActive, string RowVersion);

internal sealed record SaveLeaveTypeRequest(
    [property: Required, StringLength(20, MinimumLength = 1), RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "Use letters, digits and dashes.")] string Code,
    [property: Required, StringLength(100, MinimumLength = 1)] string Name,
    [property: Range(0, 366)] decimal? YearlyQuota,
    bool IsPaid = true,
    bool IsActive = true,
    string? RowVersion = null);

internal sealed record ApplyLeaveRequest(
    [property: Range(1, int.MaxValue)] int LeaveTypeId,
    [property: Required, RegularExpression("^(Full|HalfFirst|HalfSecond)$", ErrorMessage = "Choose Full, HalfFirst or HalfSecond.")] string DayType,
    DateOnly StartDate,
    DateOnly EndDate,
    [property: StringLength(1000)] string? Reason,
    [property: StringLength(500)] string? AddressDuringLeave,
    // HR applying on someone's behalf; ignored on the self-service route.
    int? EmployeeId = null);

internal sealed record ApplicationDto(
    int Id, int EmployeeId, string EmployeeCode, string EmployeeName, int LeaveTypeId, string LeaveType, string DayType,
    DateOnly StartDate, DateOnly EndDate, decimal Days, DateOnly AppliedOn, string? Reason, string? AddressDuringLeave,
    string Status, string CurrentStep, DateTimeOffset? Level1At, string? Level1Remarks, DateTimeOffset? DecidedAt,
    string? DecisionRemarks, bool CanDecide,int? ApproverUserId,bool UsesApprovalWorkflow);

internal sealed record DecisionRequest(bool Approve, [property: StringLength(500)] string? Remarks);
internal sealed record SaveApprovalPolicy([property: Range(0,5)] int SupervisorLevels, int? FinalApproverUserId, bool IsEnabled, string? RowVersion);
internal sealed record ReassignApproval([property:Range(1,int.MaxValue)] int NewUserId,[property:Required,StringLength(500)] string Reason,[property:Range(1,int.MaxValue)] int ExpectedApproverId);

internal sealed record AdjustmentDto(int Id, int EmployeeId, string EmployeeName, int LeaveTypeId, string LeaveType, int Year,
    decimal Days, string? Reason, DateTimeOffset CreatedAt);

internal sealed record SaveAdjustmentRequest(
    [property: Range(1, int.MaxValue)] int EmployeeId,
    [property: Range(1, int.MaxValue)] int LeaveTypeId,
    [property: Range(2000, 2200)] int Year,
    [property: Range(-366, 366)] decimal Days,
    [property: StringLength(500)] string? Reason);

/// <summary>
/// Leave management carried over from HRAccounts: leave types with yearly quotas, applications
/// with two approval steps (line manager, then HR), yearly balance adjustments and the leave
/// statement. Balance and overlap rules live in the <c>leave</c> procedures.
/// </summary>
internal static class LeaveEndpoints
{
    private const string NotLinked = "Your sign-in is not linked to an employee record. Ask HR to link it before applying for leave.";

    public static void Map(RouteGroupBuilder group)
    {
        MapTypes(group);
        MapSelfService(group);
        MapApprovals(group);
        MapAdjustments(group);
        group.MapGet("/approval-policy", async (LeaveProcedures procedures, CancellationToken ct) =>
        {
            var row = (await procedures.PolicyAsync(null, null, false, null, ct)).SingleOrDefault();
            return ApiResults.Ok(new { supervisorLevels = row?.SupervisorLevels ?? 1, finalApproverUserId = row?.FinalApproverUserId,
                isEnabled = row?.IsEnabled ?? false, rowVersion = row is null ? null : Convert.ToBase64String(row.RowVersion) });
        }).RequirePermission(LeavePermissions.SetupWrite);
        group.MapPut("/approval-policy", async (SaveApprovalPolicy request, LeaveProcedures procedures, CancellationToken ct) =>
        {
            byte[]? version = null;
            if (request.RowVersion is not null)
            {
                try { version = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return ApiResults.Fail("Reload the approval settings."); }
                if (version.Length != 8) return ApiResults.Fail("Reload the approval settings.");
            }
            await procedures.PolicyAsync(request.SupervisorLevels, request.FinalApproverUserId, request.IsEnabled, version, ct);
            return ApiResults.Ok("Approval settings saved. Existing applications keep their original route.");
        }).Validate<SaveApprovalPolicy>().RequirePermission(LeavePermissions.SetupWrite);
        group.MapGet("/applications/{id:int}/history", async (int id, LeaveProcedures procedures, CancellationToken ct) =>
            ApiResults.Ok(await procedures.HistoryAsync(id, ct))).RequireStaff();
        group.MapPost("/applications/{id:int}/reassign", async (int id,ReassignApproval request,LeaveProcedures procedures,CancellationToken ct)=>
        {
            await procedures.ReassignAsync(id,request.NewUserId,request.Reason,request.ExpectedApproverId,ct);
            return ApiResults.Ok("Current approval reassigned and recorded in history.");
        }).Validate<ReassignApproval>().RequirePermission(LeavePermissions.SetupWrite);
    }

    private static void MapTypes(RouteGroupBuilder group)
    {
        var types = group.MapGroup("/types");

        // Every signed-in user: the apply form and the filters need the list.
        types.MapGet("/", async (bool? includeInactive, LeaveDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await db.LeaveTypes.AsNoTracking().Where(t => includeInactive == true || t.IsActive)
                    .OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList()))
            .RequireStaff()
            .WithName("ListLeaveTypes");

        types.MapPost("/", async (SaveLeaveTypeRequest request, LeaveDbContext db, CancellationToken ct) =>
            {
                var code = request.Code.Trim().ToUpperInvariant();
                if (await db.LeaveTypes.AnyAsync(t => t.Code == code, ct))
                    return ApiResults.Fail("A leave type with this code already exists.", StatusCodes.Status409Conflict);
                var row = new LeaveType { Code = code, Name = request.Name.Trim(), YearlyQuota = request.YearlyQuota, IsPaid = request.IsPaid, IsActive = request.IsActive };
                db.LeaveTypes.Add(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/leave/types/{row.Id}", ToDto(row), "Leave type added.");
            })
            .Validate<SaveLeaveTypeRequest>()
            .RequirePermission(LeavePermissions.SetupWrite)
            .WithName("CreateLeaveType");

        types.MapPut("/{id:int}", async (int id, SaveLeaveTypeRequest request, LeaveDbContext db, CancellationToken ct) =>
            {
                var row = await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The leave type");
                var code = request.Code.Trim().ToUpperInvariant();
                if (await db.LeaveTypes.AnyAsync(t => t.Code == code && t.Id != id, ct))
                    return ApiResults.Fail("A leave type with this code already exists.", StatusCodes.Status409Conflict);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                row.Code = code;
                row.Name = request.Name.Trim();
                row.YearlyQuota = request.YearlyQuota;
                row.IsPaid = request.IsPaid;
                row.IsActive = request.IsActive;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Leave type saved.");
            })
            .Validate<SaveLeaveTypeRequest>()
            .RequirePermission(LeavePermissions.SetupWrite)
            .WithName("UpdateLeaveType");
    }

    private static void MapSelfService(RouteGroupBuilder group)
    {
        var my = group.MapGroup("/my").RequirePermission(LeavePermissions.ApplySelf);

        my.MapGet("/balance", async (int? year, ITenantContext tenant, IEmployeeDirectory employees, ITenantDirectory tenants,
                LeaveProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                return ApiResults.Ok(await procedures.BalanceAsync(me.Id, year ?? today.Year, ct));
            })
            .WithName("GetMyLeaveBalance");

        my.MapGet("/applications", async (int? year, ITenantContext tenant, IEmployeeDirectory employees, LeaveDbContext db,
                CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);
                var rows = await db.Applications.AsNoTracking()
                    .Where(a => a.EmployeeId == me.Id && a.IsActive && (year == null || a.StartDate.Year == year))
                    .OrderByDescending(a => a.StartDate).ThenByDescending(a => a.Id).Take(300).ToListAsync(ct);
                return ApiResults.Ok(await ToDtosAsync(rows, db, employees, _ => false, ct));
            })
            .WithName("ListMyLeaveApplications");

        my.MapPost("/applications", async (ApplyLeaveRequest request, ITenantContext tenant, IEmployeeDirectory employees,
                ITenantDirectory tenants, LeaveProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);
                return await SubmitAsync(request, me, tenants, procedures, clock, tenant.UserId.Value, ct);
            })
            .Validate<ApplyLeaveRequest>()
            .WithName("ApplyForLeave");

        my.MapPost("/applications/{id:int}/cancel", async (int id, ITenantContext tenant, IEmployeeDirectory employees,
                LeaveProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);
                await procedures.CancelAsync(id, me.Id, clock.UtcNowOffset(), tenant.UserId.Value, ct);
                return ApiResults.Ok("Leave application cancelled.");
            })
            .WithName("CancelMyLeaveApplication");
    }

    private static void MapApprovals(RouteGroupBuilder group)
    {
        // HR sees every application; a line manager without that right sees their direct reports'.
        group.MapGet("/applications", async (string? status, int? employeeId, DateOnly? from, DateOnly? to, int? page, int? pageSize,
                ITenantContext tenant, IPermissionProvider permissions, IEmployeeDirectory employees, LeaveDbContext db,
                CancellationToken ct) =>
            {
                var caller = await CallerAsync(tenant, permissions, employees, ct);
                var paging = new PageRequest(page ?? 1, pageSize ?? 20);
                var query = db.Applications.AsNoTracking().Where(a => a.IsActive);
                if (!caller.SeesAll)
                    query = query.Where(a => caller.ReportIds.Contains(a.EmployeeId) || a.ApproverUserId == tenant.UserId);
                if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
                if (employeeId is { } id) query = query.Where(a => a.EmployeeId == id);
                if (from is { } start) query = query.Where(a => a.EndDate >= start);
                if (to is { } end) query = query.Where(a => a.StartDate <= end);

                var total = await query.CountAsync(ct);
                var rows = await query.OrderBy(a => a.Status == "Pending" ? 0 : 1).ThenByDescending(a => a.AppliedOn).ThenByDescending(a => a.Id)
                    .Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);
                return ApiResults.Ok(new PagedResult<ApplicationDto>(await ToDtosAsync(rows, db, employees, caller.CanDecide, ct),
                    total, paging.SafePage, paging.SafePageSize));
            })
            .RequirePermission(LeavePermissions.ApplicationsRead, LeavePermissions.ApproveLevel1, LeavePermissions.ApproveLevel2, LeavePermissions.ApproveLevel3, LeavePermissions.ApproveLevel4, LeavePermissions.ApproveLevel5, LeavePermissions.ApproveFinal)
            .WithName("ListLeaveApplications");

        // HR enters an application on an employee's behalf (a phoned-in sick day, for instance).
        group.MapPost("/applications", async (ApplyLeaveRequest request, ITenantContext tenant, IEmployeeDirectory employees,
                ITenantDirectory tenants, LeaveProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (request.EmployeeId is not { } employeeId || await employees.GetAsync(employeeId, ct) is not { IsActive: true } employee)
                    return ApiResults.NotFound("The employee");
                return await SubmitAsync(request, employee, tenants, procedures, clock, tenant.UserId!.Value, ct);
            })
            .Validate<ApplyLeaveRequest>()
            .RequirePermission(LeavePermissions.ApproveLevel2)
            .WithName("ApplyForLeaveOnBehalf");

        group.MapPost("/applications/{id:int}/decision", async (int id, DecisionRequest request, ITenantContext tenant,
                IPermissionProvider permissions, IEmployeeDirectory employees, LeaveDbContext db, LeaveProcedures procedures,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                var caller = await CallerAsync(tenant, permissions, employees, ct);
                var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.IsActive, ct);
                if (application is null)
                    return ApiResults.NotFound("The leave application");
                if (!caller.CanDecide(application))
                    return ApiResults.Fail("You cannot decide this application at its current step.", StatusCodes.Status403Forbidden);

                var decided = application.UsesApprovalWorkflow
                    ? await procedures.WorkflowDecideAsync(id, request.Approve, request.Remarks, clock.UtcNowOffset(), tenant.UserId!.Value, ct)
                    : await procedures.DecideAsync(id, request.Approve, caller.Level1, caller.Level2, caller.EmployeeId,
                        request.Remarks, clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                return ApiResults.Ok(decided, decided.Status switch
                {
                    "Approved" => "Leave approved.",
                    "Rejected" => "Leave rejected.",
                    _ => $"Approved and sent to {decided.CurrentStep} for the next decision."
                });
            })
            .Validate<DecisionRequest>()
            .RequirePermission(LeavePermissions.ApproveLevel1, LeavePermissions.ApproveLevel2, LeavePermissions.ApproveLevel3, LeavePermissions.ApproveLevel4, LeavePermissions.ApproveLevel5, LeavePermissions.ApproveFinal)
            .WithName("DecideLeaveApplication");

        group.MapGet("/balance/{employeeId:int}", async (int employeeId, int? year, ITenantDirectory tenants, LeaveProcedures procedures,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                return ApiResults.Ok(await procedures.BalanceAsync(employeeId, year ?? today.Year, ct));
            })
            .RequirePermission(LeavePermissions.ApplicationsRead, LeavePermissions.ApproveLevel2, LeavePermissions.Adjust)
            .WithName("GetEmployeeLeaveBalance");

        group.MapGet("/statement", async (int? year, int? departmentId, ITenantDirectory tenants, LeaveProcedures procedures,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                return ApiResults.Ok(await procedures.StatementAsync(year ?? today.Year, departmentId, null, ct));
            })
            .RequirePermission(LeavePermissions.ReportsRead)
            .WithName("GetLeaveStatement");
    }

    private static void MapAdjustments(RouteGroupBuilder group)
    {
        var adjustments = group.MapGroup("/adjustments").RequirePermission(LeavePermissions.Adjust);

        adjustments.MapGet("/", async (int? year, int? employeeId, LeaveDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                var rows = await db.Adjustments.AsNoTracking()
                    .Where(a => a.IsActive && (year == null || a.Year == year) && (employeeId == null || a.EmployeeId == employeeId))
                    .OrderByDescending(a => a.Id).Take(500).ToListAsync(ct);
                var people = await employees.GetManyAsync(rows.Select(r => r.EmployeeId).ToList(), ct);
                var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
                return ApiResults.Ok(rows.Select(a => new AdjustmentDto(a.Id, a.EmployeeId,
                    people.GetValueOrDefault(a.EmployeeId)?.FullName ?? $"#{a.EmployeeId}", a.LeaveTypeId,
                    types.GetValueOrDefault(a.LeaveTypeId) ?? string.Empty, a.Year, a.Days, a.Reason, a.CreatedAt)).ToList());
            })
            .WithName("ListLeaveAdjustments");

        adjustments.MapPost("/", async (SaveAdjustmentRequest request, LeaveDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                if (request.Days == 0 || request.Days * 2 != Math.Round(request.Days * 2))
                    return ApiResults.Fail("Enter the days in halves, and not zero.", StatusCodes.Status422UnprocessableEntity);
                if (await employees.GetAsync(request.EmployeeId, ct) is null)
                    return ApiResults.NotFound("The employee");
                if (!await db.LeaveTypes.AnyAsync(t => t.Id == request.LeaveTypeId, ct))
                    return ApiResults.NotFound("The leave type");
                db.Adjustments.Add(new LeaveAdjustment
                {
                    EmployeeId = request.EmployeeId, LeaveTypeId = request.LeaveTypeId, Year = request.Year, Days = request.Days,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
                });
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Balance adjusted.");
            })
            .Validate<SaveAdjustmentRequest>()
            .WithName("CreateLeaveAdjustment");

        adjustments.MapDelete("/{id:int}", async (int id, LeaveDbContext db, CancellationToken ct) =>
            {
                var row = await db.Adjustments.SingleOrDefaultAsync(a => a.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The adjustment");
                row.IsActive = false;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Adjustment removed.");
            })
            .WithName("DeleteLeaveAdjustment");
    }

    private static async Task<IResult> SubmitAsync(ApplyLeaveRequest request, EmployeeRef employee, ITenantDirectory tenants,
        LeaveProcedures procedures, IAppTimeProvider clock, int actorId, CancellationToken ct)
    {
        var now = clock.UtcNowOffset();
        var today = (await tenants.GetClockAsync(ct)).DateOf(now);
        // Nobody to take the manager step: the application goes straight to HR.
        var submitted = await procedures.SubmitAsync(employee.Id, request.LeaveTypeId, request.DayType, request.StartDate,
            request.EndDate, request.Reason, request.AddressDuringLeave, employee.ReportsToId is null, today, now, actorId, ct);
        return ApiResults.Ok(submitted, $"Leave applied for {submitted.Days:0.#} day(s).");
    }

    /// <summary>What the signed-in user may see and decide.</summary>
    private sealed record Caller(int UserId, int? EmployeeId, bool Level1, bool Level2, bool SeesAll, IReadOnlySet<int> ReportIds, IReadOnlySet<string> Permissions)
    {
        /// <summary>
        /// HR (level 2) decides any pending application, at either step. A line manager decides the
        /// manager step of a direct report, never their own application.
        /// </summary>
        public bool CanDecide(LeaveApplication a)
            => a.Status == "Pending"
               && (a.UsesApprovalWorkflow
                   ? a.ApproverUserId == UserId && a.EmployeeId != EmployeeId && Permissions.Contains(a.CurrentStep == "Final" ? LeavePermissions.ApproveFinal : "leave.approve." + a.CurrentStep.ToLowerInvariant())
                   : Level2 || (Level1 && a.CurrentStep == "Manager" && a.EmployeeId != EmployeeId && ReportIds.Contains(a.EmployeeId)));
    }

    private static async Task<Caller> CallerAsync(ITenantContext tenant, IPermissionProvider permissions, IEmployeeDirectory employees,
        CancellationToken ct)
    {
        var granted = await permissions.GetPermissionsAsync(tenant.RequiredTenantId, tenant.UserId!.Value, ct);
        var me = await employees.GetByUserAsync(tenant.UserId.Value, ct);
        var reports = me is null ? [] : (await employees.GetDirectReportIdsAsync(me.Id, ct)).ToHashSet();
        var level2 = granted.Contains(LeavePermissions.ApproveLevel2);
        return new Caller(tenant.UserId.Value, me?.Id, granted.Contains(LeavePermissions.ApproveLevel1), level2,
            level2 || granted.Contains(LeavePermissions.ApplicationsRead), reports, granted);
    }

    private static async Task<List<ApplicationDto>> ToDtosAsync(IReadOnlyList<LeaveApplication> rows, LeaveDbContext db,
        IEmployeeDirectory employees, Func<LeaveApplication, bool> canDecide, CancellationToken ct)
    {
        var people = await employees.GetManyAsync(rows.Select(r => r.EmployeeId).ToList(), ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return rows.Select(a =>
        {
            var person = people.GetValueOrDefault(a.EmployeeId);
            return new ApplicationDto(a.Id, a.EmployeeId, person?.Code ?? string.Empty, person?.FullName ?? $"#{a.EmployeeId}",
                a.LeaveTypeId, types.GetValueOrDefault(a.LeaveTypeId) ?? string.Empty, a.DayType, a.StartDate, a.EndDate, a.Days,
                a.AppliedOn, a.Reason, a.AddressDuringLeave, a.Status, a.CurrentStep, a.Level1At, a.Level1Remarks, a.DecidedAt,
                a.DecisionRemarks, canDecide(a),a.ApproverUserId,a.UsesApprovalWorkflow);
        }).ToList();
    }

    private static LeaveTypeDto ToDto(LeaveType t)
        => new(t.Id, t.Code, t.Name, t.YearlyQuota, t.IsPaid, t.IsActive, Convert.ToBase64String(t.RowVersion));
}
