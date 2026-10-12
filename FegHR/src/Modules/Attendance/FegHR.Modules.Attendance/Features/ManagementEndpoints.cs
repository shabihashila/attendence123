using System.ComponentModel.DataAnnotations;
using FegHR.Modules.Attendance.Domain;
using FegHR.Modules.Attendance.Persistence;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Shared.Time;

namespace FegHR.Modules.Attendance.Features;

internal sealed record ManualLogRequest(
    [property: Range(1, int.MaxValue)] int EmployeeId,
    DateOnly LogDate,
    TimeOnly? InTime,
    TimeOnly? OutTime,
    [property: StringLength(500)] string? Remarks);

internal sealed record ReviewRow(
    long Id, int EmployeeId, string EmployeeCode, string EmployeeName, DateTimeOffset PunchedAt, string LogDate, string LocalTime,
    string Direction, string Source, string LocationStatus, decimal? Latitude, decimal? Longitude, double? AccuracyMeters,
    double? DistanceMeters, string? LocationName, string? IpAddress, string? DeviceId, string? Platform, bool HasPhoto,
    IReadOnlyList<string> Flags, bool IsReviewed, string? ReviewNote, string? Remarks);

internal sealed record ReviewRequest(
    [property: StringLength(30)] string? NewLocationStatus,
    [property: StringLength(500)] string? Note);

internal sealed record DeviceDto(int Id, int EmployeeId, string EmployeeName, string DeviceId, string? Label, string? Platform,
    bool IsApproved, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

/// <summary>HR's side of attendance: the day and month views, manual corrections and the review queue.</summary>
internal static class ManagementEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/daily", async (DateOnly? date, int? branchId, int? departmentId, ITenantDirectory tenants,
                AttendanceProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                return ApiResults.Ok(await procedures.DailyAsync(date ?? today, today, branchId, departmentId, ct));
            })
            .RequirePermission(AttendancePermissions.LogsRead)
            .WithName("GetDailyAttendance");

        group.MapGet("/monthly", async (DateOnly? from, DateOnly? to, int? branchId, int? departmentId, ITenantDirectory tenants,
                AttendanceProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                var start = from ?? new DateOnly(today.Year, today.Month, 1);
                var end = to ?? start.AddMonths(1).AddDays(-1);
                if (end < start || end.DayNumber - start.DayNumber > 92)
                    return ApiResults.Fail("Choose a period of at most three months.");
                return ApiResults.Ok(await procedures.MonthlyAsync(start, end, today, branchId, departmentId, ct));
            })
            .RequirePermission(AttendancePermissions.ReportsRead)
            .WithName("GetMonthlyAttendance");

        group.MapGet("/employees/{employeeId:int}/days", async (int employeeId, DateOnly? from, DateOnly? to,
                ITenantDirectory tenants, AttendanceProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                var start = from ?? new DateOnly(today.Year, today.Month, 1);
                var end = to ?? start.AddMonths(1).AddDays(-1);
                if (end < start)
                    return ApiResults.Fail("The end date is before the start date.");
                return ApiResults.Ok(await procedures.EmployeeDaysAsync(employeeId, start, end, today, ct));
            })
            .RequirePermission(AttendancePermissions.LogsRead)
            .WithName("GetEmployeeAttendanceDays");

        // HR keys a day in by hand. A manual row is authoritative: later punches never overwrite it.
        group.MapPut("/logs", async (ManualLogRequest request, AttendanceDbContext db, IEmployeeDirectory employees,
                ITenantDirectory tenants, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetAsync(request.EmployeeId, ct) is null)
                    return ApiResults.NotFound("The employee");
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                if (request.LogDate > today)
                    return ApiResults.Fail("Attendance cannot be entered for a future date.", StatusCodes.Status422UnprocessableEntity);
                if (request.InTime is null && request.OutTime is not null)
                    return ApiResults.Fail("Enter the check-in time as well.", StatusCodes.Status422UnprocessableEntity);
                if (request.InTime is { } inAt && request.OutTime is { } outAt && outAt <= inAt)
                    return ApiResults.Fail("The check-out must be after the check-in.", StatusCodes.Status422UnprocessableEntity);

                var row = await db.DailyLogs.SingleOrDefaultAsync(l => l.EmployeeId == request.EmployeeId && l.LogDate == request.LogDate, ct);
                if (row is null)
                    db.DailyLogs.Add(row = new DailyLog { EmployeeId = request.EmployeeId, LogDate = request.LogDate });
                row.InTime = request.InTime;
                row.OutTime = request.OutTime;
                row.Source = LogSources.Manual;
                row.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
                row.IsActive = true;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Attendance saved.");
            })
            .Validate<ManualLogRequest>()
            .RequirePermission(AttendancePermissions.LogsWrite)
            .WithName("SaveManualAttendance");

        // Removes a manual correction, so the day is derived from the punches again.
        group.MapDelete("/logs", async (int employeeId, DateOnly logDate, AttendanceDbContext db, AttendanceProcedures procedures,
                ITenantContext tenant, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var row = await db.DailyLogs.SingleOrDefaultAsync(l => l.EmployeeId == employeeId && l.LogDate == logDate, ct);
                if (row is null || row.Source != LogSources.Manual)
                    return ApiResults.NotFound("The manual entry");
                db.DailyLogs.Remove(row);
                await db.SaveChangesAsync(ct);
                if (await db.Punches.AnyAsync(p => p.EmployeeId == employeeId && p.LogDate == logDate && p.IsActive, ct))
                    await procedures.ReconcileAsync(employeeId, logDate, "Web", clock.UtcNowOffset(), tenant.UserId!.Value, ct);
                return ApiResults.Ok("Manual entry removed.");
            })
            .RequirePermission(AttendancePermissions.LogsWrite)
            .WithName("DeleteManualAttendance");

        // Only punches that need a human: an in-office punch is not review work.
        group.MapGet("/review", async (DateOnly? from, DateOnly? to, bool? includeReviewed, int? page, int? pageSize,
                AttendanceDbContext db, IEmployeeDirectory employees, ITenantDirectory tenants, IAppTimeProvider clock,
                CancellationToken ct) =>
            {
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                var start = from ?? today.AddDays(-30);
                var end = to ?? today;
                var paging = new PageRequest(page ?? 1, pageSize ?? 20);
                var query = db.Punches.AsNoTracking().Where(p => p.IsActive && p.LogDate >= start && p.LogDate <= end
                    && LocationStatuses.NeedsReview.Contains(p.LocationStatus));
                if (includeReviewed != true)
                    query = query.Where(p => !p.IsReviewed);
                var total = await query.CountAsync(ct);
                var rows = await query.OrderByDescending(p => p.PunchedAt).Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);
                return ApiResults.Ok(new PagedResult<ReviewRow>(await ToReviewRowsAsync(rows, db, employees, ct), total,
                    paging.SafePage, paging.SafePageSize));
            })
            .RequirePermission(AttendancePermissions.Review)
            .WithName("GetPunchReviewQueue");

        // HR may reclassify the verdict. The captured observations stay exactly as recorded.
        group.MapPut("/review/{id:long}", async (long id, ReviewRequest request, AttendanceDbContext db, ITenantContext tenant,
                IEmployeeDirectory employees, IAppTimeProvider clock, CancellationToken ct) =>
            {
                var punch = await db.Punches.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (punch is null)
                    return ApiResults.NotFound("The punch");
                if (!string.IsNullOrWhiteSpace(request.NewLocationStatus))
                {
                    if (!LocationStatuses.All.Contains(request.NewLocationStatus))
                        return ApiResults.Fail("Unknown location status.");
                    punch.LocationStatus = request.NewLocationStatus;
                }
                punch.IsReviewed = true;
                punch.ReviewedBy = tenant.UserId;
                punch.ReviewedAt = clock.UtcNowOffset();
                punch.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok((await ToReviewRowsAsync([punch], db, employees, ct))[0], "Punch reviewed.");
            })
            .Validate<ReviewRequest>()
            .RequirePermission(AttendancePermissions.Review)
            .WithName("ReviewPunch");

        group.MapGet("/devices", async (bool? pendingOnly, AttendanceDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                var rows = await db.EmployeeDevices.AsNoTracking().Where(d => d.IsActive && (pendingOnly != true || !d.IsApproved))
                    .OrderByDescending(d => d.LastSeenAt).Take(500).ToListAsync(ct);
                var people = await employees.GetManyAsync(rows.Select(r => r.EmployeeId).ToList(), ct);
                return ApiResults.Ok(rows.Select(d => new DeviceDto(d.Id, d.EmployeeId,
                    people.GetValueOrDefault(d.EmployeeId)?.FullName ?? $"#{d.EmployeeId}", d.DeviceId, d.Label, d.Platform,
                    d.IsApproved, d.FirstSeenAt, d.LastSeenAt)).ToList());
            })
            .RequirePermission(AttendancePermissions.Review)
            .WithName("ListEmployeeDevices");

        group.MapPut("/devices/{id:int}/approval", async (int id, bool approved, AttendanceDbContext db, ITenantContext tenant,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                var device = await db.EmployeeDevices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                    return ApiResults.NotFound("The device");
                device.IsApproved = approved;
                device.ApprovedBy = approved ? tenant.UserId : null;
                device.ApprovedAt = approved ? clock.UtcNowOffset() : null;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(approved ? "Device approved." : "Device approval removed.");
            })
            .RequirePermission(AttendancePermissions.Review)
            .WithName("SetDeviceApproval");
    }

    private static async Task<List<ReviewRow>> ToReviewRowsAsync(IReadOnlyList<Punch> rows, AttendanceDbContext db,
        IEmployeeDirectory employees, CancellationToken ct)
    {
        var people = await employees.GetManyAsync(rows.Select(r => r.EmployeeId).ToList(), ct);
        var names = await SelfServiceEndpoints.LocationNamesAsync(db, rows, ct);
        return rows.Select(p =>
        {
            var person = people.GetValueOrDefault(p.EmployeeId);
            return new ReviewRow(p.Id, p.EmployeeId, person?.Code ?? string.Empty, person?.FullName ?? $"#{p.EmployeeId}",
                p.PunchedAt, p.LogDate.ToString("yyyy-MM-dd"), p.LocalTime.ToString("HH:mm"), p.Direction, p.Source, p.LocationStatus,
                p.Latitude, p.Longitude, p.AccuracyMeters, p.DistanceMeters,
                p.MatchedLocationId is { } id ? names.GetValueOrDefault(id) : null, p.IpAddress, p.DeviceId, p.Platform,
                p.SelfieKey is not null, SelfServiceEndpoints.Flags(p.FlagsJson), p.IsReviewed, p.ReviewNote, p.Remarks);
        }).ToList();
    }
}
