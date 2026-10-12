using System.ComponentModel.DataAnnotations;
using System.Net;
using FegHR.Modules.Attendance.Domain;
using FegHR.Modules.Attendance.Persistence;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Attendance.Features;

internal sealed record SettingsDto(bool RequireSelfie, bool RequireLocation, string GeofenceMode, int MaxAccuracyMeters,
    int DebounceSeconds, int MinInToOutMinutes, string? RowVersion);

internal sealed record SaveSettingsRequest(
    bool RequireSelfie,
    bool RequireLocation,
    [property: Required, RegularExpression("^(Enforce|Flag|Off)$", ErrorMessage = "Choose Enforce, Flag or Off.")] string GeofenceMode,
    [property: Range(10, 5000)] int MaxAccuracyMeters,
    [property: Range(0, 600)] int DebounceSeconds,
    [property: Range(0, 240)] int MinInToOutMinutes,
    string? RowVersion);

internal sealed record LocationDto(int Id, string Name, string LocationType, int? BranchId, string? Branch, decimal? Latitude,
    decimal? Longitude, int RadiusMeters, string? AllowedPublicIps, string? Address, bool IsActive, string RowVersion);

internal sealed record SaveLocationRequest(
    [property: Required, StringLength(150, MinimumLength = 1)] string Name,
    [property: Required, RegularExpression("^(Office|Site|RemoteAllowed)$", ErrorMessage = "Choose Office, Site or RemoteAllowed.")] string LocationType,
    int? BranchId,
    [property: Range(-90, 90)] decimal? Latitude,
    [property: Range(-180, 180)] decimal? Longitude,
    [property: Range(10, 100000)] int RadiusMeters,
    [property: StringLength(500)] string? AllowedPublicIps,
    [property: StringLength(500)] string? Address,
    bool IsActive = true,
    string? RowVersion = null);

internal sealed record ScheduleDayDto(
    [property: Range(0, 6)] int DayOfWeek, bool IsWorkingDay, TimeOnly StartTime, TimeOnly EndTime, [property: Range(0, 480)] int GraceMinutes);

internal sealed record ScheduleDto(int Id, string Name, string? Description, bool IsDefault, bool IsActive,
    IReadOnlyList<ScheduleDayDto> Days, string RowVersion);

internal sealed record SaveScheduleRequest(
    [property: Required, StringLength(150, MinimumLength = 1)] string Name,
    [property: StringLength(500)] string? Description,
    bool IsDefault,
    [property: Required] IReadOnlyList<ScheduleDayDto> Days,
    bool IsActive = true,
    string? RowVersion = null);

internal sealed record EmployeeScheduleDto(int Id, int EmployeeId, string EmployeeName, int WorkScheduleId, string ScheduleName,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Remarks);

internal sealed record AssignScheduleRequest(
    [property: Range(1, int.MaxValue)] int EmployeeId,
    [property: Range(1, int.MaxValue)] int WorkScheduleId,
    DateOnly EffectiveFrom,
    [property: StringLength(500)] string? Remarks);

internal sealed record HolidayDto(int Id, string Title, DateOnly HolidayDate, string? Remarks, string RowVersion);

internal sealed record SaveHolidayRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Title,
    DateOnly HolidayDate,
    [property: StringLength(500)] string? Remarks,
    string? RowVersion = null);

/// <summary>What the company configures: punch rules, geofences, shifts and holidays.</summary>
internal static class SetupEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        MapSettings(group);
        MapLocations(group);
        MapSchedules(group);
        MapHolidays(group);
    }

    private static void MapSettings(RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (AttendanceDbContext db, CancellationToken ct) =>
                ApiResults.Ok(ToDto(await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AttendanceSetting())))
            .RequirePermission(AttendancePermissions.SetupRead, AttendancePermissions.SetupWrite)
            .WithName("GetAttendanceSettings");

        group.MapPut("/settings", async (SaveSettingsRequest request, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.Settings.FirstOrDefaultAsync(ct);
                if (row is null)
                    db.Settings.Add(row = new AttendanceSetting());
                else if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                row.RequireSelfie = request.RequireSelfie;
                row.RequireLocation = request.RequireLocation;
                row.GeofenceMode = request.GeofenceMode;
                row.MaxAccuracyMeters = request.MaxAccuracyMeters;
                row.DebounceSeconds = request.DebounceSeconds;
                row.MinInToOutMinutes = request.MinInToOutMinutes;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Attendance rules saved.");
            })
            .Validate<SaveSettingsRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("SaveAttendanceSettings");
    }

    private static void MapLocations(RouteGroupBuilder group)
    {
        var locations = group.MapGroup("/locations");

        locations.MapGet("/", async (AttendanceDbContext db, ITenantDirectory tenants, CancellationToken ct) =>
            {
                var branches = (await tenants.GetBranchesAsync(activeOnly: false, ct)).ToDictionary(b => b.Id, b => b.Name);
                return ApiResults.Ok((await db.WorkLocations.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct))
                    .Select(w => ToDto(w, branches)).ToList());
            })
            .RequirePermission(AttendancePermissions.SetupRead)
            .WithName("ListWorkLocations");

        locations.MapPost("/", async (SaveLocationRequest request, AttendanceDbContext db, ITenantDirectory tenants, CancellationToken ct) =>
            {
                if (await ProblemAsync(request, null, db, tenants, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                var row = new WorkLocation();
                Apply(row, request);
                db.WorkLocations.Add(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/attendance/locations/{row.Id}", ToDto(row, await BranchNamesAsync(tenants, ct)), "Work location added.");
            })
            .Validate<SaveLocationRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("CreateWorkLocation");

        locations.MapPut("/{id:int}", async (int id, SaveLocationRequest request, AttendanceDbContext db, ITenantDirectory tenants,
                CancellationToken ct) =>
            {
                var row = await db.WorkLocations.SingleOrDefaultAsync(w => w.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The work location");
                if (await ProblemAsync(request, id, db, tenants, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                Apply(row, request);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row, await BranchNamesAsync(tenants, ct)), "Work location saved.");
            })
            .Validate<SaveLocationRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("UpdateWorkLocation");

        // Punches keep pointing at the location they matched, so a used location is deactivated, not removed.
        locations.MapDelete("/{id:int}", async (int id, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.WorkLocations.SingleOrDefaultAsync(w => w.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The work location");
                if (await db.Punches.AnyAsync(p => p.MatchedLocationId == id, ct))
                {
                    row.IsActive = false;
                    await db.SaveChangesAsync(ct);
                    return ApiResults.Ok("The location has attendance history, so it was deactivated instead.");
                }
                db.WorkLocations.Remove(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Work location deleted.");
            })
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("DeleteWorkLocation");
    }

    private static void MapSchedules(RouteGroupBuilder group)
    {
        var schedules = group.MapGroup("/schedules");

        schedules.MapGet("/", async (AttendanceDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await db.WorkSchedules.AsNoTracking().Include(s => s.Days).OrderBy(s => s.Name).ToListAsync(ct))
                    .Select(ToDto).ToList()))
            .RequirePermission(AttendancePermissions.SetupRead)
            .WithName("ListWorkSchedules");

        schedules.MapPost("/", async (SaveScheduleRequest request, AttendanceDbContext db, CancellationToken ct) =>
            {
                if (await ProblemAsync(request, null, db, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await ClearOtherDefaultAsync(request, null, db, ct);
                var row = new WorkSchedule();
                Apply(row, request);
                db.WorkSchedules.Add(row);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return ApiResults.Created($"/api/attendance/schedules/{row.Id}", ToDto(row), "Work schedule added.");
            })
            .Validate<SaveScheduleRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("CreateWorkSchedule");

        schedules.MapPut("/{id:int}", async (int id, SaveScheduleRequest request, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.WorkSchedules.Include(s => s.Days).SingleOrDefaultAsync(s => s.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The work schedule");
                if (await ProblemAsync(request, id, db, ct) is { } problem)
                    return ApiResults.Fail(problem, StatusCodes.Status422UnprocessableEntity);
                if (row.IsDefault && (!request.IsDefault || !request.IsActive))
                    return ApiResults.Fail("Make another schedule the default first; a company always has one.",
                        StatusCodes.Status422UnprocessableEntity);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await ClearOtherDefaultAsync(request, id, db, ct);
                Apply(row, request);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return ApiResults.Ok(ToDto(row), "Work schedule saved.");
            })
            .Validate<SaveScheduleRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("UpdateWorkSchedule");

        var assignments = group.MapGroup("/employee-schedules");

        assignments.MapGet("/", async (int? employeeId, AttendanceDbContext db, IEmployeeDirectory employees, CancellationToken ct) =>
            {
                var rows = await db.EmployeeSchedules.AsNoTracking().Where(e => e.IsActive && (employeeId == null || e.EmployeeId == employeeId))
                    .OrderByDescending(e => e.EffectiveFrom).Take(500).ToListAsync(ct);
                var people = await employees.GetManyAsync(rows.Select(r => r.EmployeeId).ToList(), ct);
                var names = await db.WorkSchedules.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
                return ApiResults.Ok(rows.Select(r => new EmployeeScheduleDto(r.Id, r.EmployeeId,
                    people.GetValueOrDefault(r.EmployeeId)?.FullName ?? $"#{r.EmployeeId}", r.WorkScheduleId,
                    names.GetValueOrDefault(r.WorkScheduleId) ?? string.Empty, r.EffectiveFrom, r.EffectiveTo, r.Remarks)).ToList());
            })
            .RequirePermission(AttendancePermissions.SetupRead)
            .WithName("ListEmployeeSchedules");

        // A new assignment closes the employee's open one the day before it starts.
        assignments.MapPost("/", async (AssignScheduleRequest request, AttendanceDbContext db, IEmployeeDirectory employees,
                CancellationToken ct) =>
            {
                if (await employees.GetAsync(request.EmployeeId, ct) is null)
                    return ApiResults.NotFound("The employee");
                if (!await db.WorkSchedules.AnyAsync(s => s.Id == request.WorkScheduleId && s.IsActive, ct))
                    return ApiResults.NotFound("The work schedule");
                var open = await db.EmployeeSchedules.Where(e => e.EmployeeId == request.EmployeeId && e.IsActive && e.EffectiveTo == null).ToListAsync(ct);
                if (open.Any(o => o.EffectiveFrom >= request.EffectiveFrom))
                    return ApiResults.Fail("The employee already has a schedule starting on or after that date.",
                        StatusCodes.Status422UnprocessableEntity);
                foreach (var previous in open)
                    previous.EffectiveTo = request.EffectiveFrom.AddDays(-1);
                db.EmployeeSchedules.Add(new EmployeeSchedule
                {
                    EmployeeId = request.EmployeeId, WorkScheduleId = request.WorkScheduleId, EffectiveFrom = request.EffectiveFrom,
                    Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
                });
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Schedule assigned.");
            })
            .Validate<AssignScheduleRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("AssignEmployeeSchedule");

        assignments.MapDelete("/{id:int}", async (int id, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.EmployeeSchedules.SingleOrDefaultAsync(e => e.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The assignment");
                row.IsActive = false;
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Assignment removed. The company default applies again.");
            })
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("RemoveEmployeeSchedule");
    }

    private static void MapHolidays(RouteGroupBuilder group)
    {
        var holidays = group.MapGroup("/holidays");

        holidays.MapGet("/", async (int? year, AttendanceDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await db.Holidays.AsNoTracking().Where(h => h.IsActive && (year == null || h.HolidayDate.Year == year))
                    .OrderBy(h => h.HolidayDate).ToListAsync(ct)).Select(ToDto).ToList()))
            .RequirePermission(AttendancePermissions.SetupRead)
            .WithName("ListHolidays");

        holidays.MapPost("/", async (SaveHolidayRequest request, AttendanceDbContext db, CancellationToken ct) =>
            {
                var title = request.Title.Trim();
                if (await db.Holidays.AnyAsync(h => h.HolidayDate == request.HolidayDate && h.Title == title, ct))
                    return ApiResults.Fail("That holiday is already on the calendar.", StatusCodes.Status409Conflict);
                var row = new Holiday { Title = title, HolidayDate = request.HolidayDate, Remarks = Clean(request.Remarks) };
                db.Holidays.Add(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/attendance/holidays/{row.Id}", ToDto(row), "Holiday added.");
            })
            .Validate<SaveHolidayRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("CreateHoliday");

        holidays.MapPut("/{id:int}", async (int id, SaveHolidayRequest request, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The holiday");
                var title = request.Title.Trim();
                if (await db.Holidays.AnyAsync(h => h.HolidayDate == request.HolidayDate && h.Title == title && h.Id != id, ct))
                    return ApiResults.Fail("That holiday is already on the calendar.", StatusCodes.Status409Conflict);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                row.Title = title;
                row.HolidayDate = request.HolidayDate;
                row.Remarks = Clean(request.Remarks);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Holiday saved.");
            })
            .Validate<SaveHolidayRequest>()
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("UpdateHoliday");

        holidays.MapDelete("/{id:int}", async (int id, AttendanceDbContext db, CancellationToken ct) =>
            {
                var row = await db.Holidays.SingleOrDefaultAsync(h => h.Id == id, ct);
                if (row is null)
                    return ApiResults.NotFound("The holiday");
                db.Holidays.Remove(row);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok("Holiday deleted.");
            })
            .RequirePermission(AttendancePermissions.SetupWrite)
            .WithName("DeleteHoliday");
    }

    // ---- work locations ----

    private static async Task<string?> ProblemAsync(SaveLocationRequest r, int? id, AttendanceDbContext db, ITenantDirectory tenants,
        CancellationToken ct)
    {
        var name = r.Name.Trim();
        if (await db.WorkLocations.AnyAsync(w => w.Name == name && w.Id != id, ct))
            return "Another work location already has this name.";
        if (r.Latitude is null != r.Longitude is null)
            return "Enter both the latitude and the longitude, or neither.";
        var ips = Ips(r.AllowedPublicIps);
        if (ips is null)
            return "The allowed IP list must be IP addresses separated by commas.";
        if (r.Latitude is null && ips.Count == 0)
            return "A work location needs a map position, or at least one office IP address.";
        if (r.BranchId is { } branchId && (await tenants.GetBranchesAsync(activeOnly: false, ct)).All(b => b.Id != branchId))
            return "The branch does not exist.";
        return null;
    }

    private static void Apply(WorkLocation row, SaveLocationRequest r)
    {
        row.Name = r.Name.Trim();
        row.LocationType = r.LocationType;
        row.BranchId = r.BranchId;
        row.Latitude = r.Latitude is { } lat ? Math.Round(lat, 6) : null;
        row.Longitude = r.Longitude is { } lng ? Math.Round(lng, 6) : null;
        row.RadiusMeters = r.RadiusMeters;
        var ips = Ips(r.AllowedPublicIps) ?? [];
        row.AllowedPublicIps = ips.Count == 0 ? null : string.Join(",", ips);
        row.Address = Clean(r.Address);
        row.IsActive = r.IsActive;
    }

    /// <summary>The normalized addresses, or null when an entry is not an IP address.</summary>
    private static List<string>? Ips(string? value)
    {
        var result = new List<string>();
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(part, out var address))
                return null;
            result.Add(address.ToString());
        }
        return result;
    }

    private static async Task<Dictionary<int, string>> BranchNamesAsync(ITenantDirectory tenants, CancellationToken ct)
        => (await tenants.GetBranchesAsync(activeOnly: false, ct)).ToDictionary(b => b.Id, b => b.Name);

    private static LocationDto ToDto(WorkLocation w, Dictionary<int, string> branches)
        => new(w.Id, w.Name, w.LocationType, w.BranchId, w.BranchId is { } b ? branches.GetValueOrDefault(b) : null, w.Latitude,
            w.Longitude, w.RadiusMeters, w.AllowedPublicIps, w.Address, w.IsActive, Convert.ToBase64String(w.RowVersion));

    // ---- schedules ----

    private static async Task<string?> ProblemAsync(SaveScheduleRequest r, int? id, AttendanceDbContext db, CancellationToken ct)
    {
        var name = r.Name.Trim();
        if (await db.WorkSchedules.AnyAsync(s => s.Name == name && s.Id != id, ct))
            return "Another work schedule already has this name.";
        if (r.Days.Count != 7 || r.Days.Select(d => d.DayOfWeek).Distinct().Count() != 7 || r.Days.Any(d => d.DayOfWeek is < 0 or > 6))
            return "A schedule needs exactly one row for each day of the week.";
        if (r.Days.Any(d => d.IsWorkingDay && d.EndTime <= d.StartTime))
            return "A working day must end after it starts.";
        if (r.Days.Any(d => d.GraceMinutes is < 0 or > 480))
            return "Grace minutes run from 0 to 480.";
        if (!r.Days.Any(d => d.IsWorkingDay))
            return "A schedule needs at least one working day.";
        if (r.IsDefault && !r.IsActive)
            return "The default schedule must be active.";
        return null;
    }

    // The filtered unique index allows one default per company, so the old one is cleared first.
    private static async Task ClearOtherDefaultAsync(SaveScheduleRequest r, int? id, AttendanceDbContext db, CancellationToken ct)
    {
        if (!r.IsDefault)
            return;
        foreach (var other in await db.WorkSchedules.Where(s => s.IsDefault && s.Id != id).ToListAsync(ct))
            other.IsDefault = false;
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(WorkSchedule row, SaveScheduleRequest r)
    {
        row.Name = r.Name.Trim();
        row.Description = Clean(r.Description);
        row.IsDefault = r.IsDefault;
        row.IsActive = r.IsActive;
        foreach (var day in r.Days)
        {
            var existing = row.Days.FirstOrDefault(d => d.DayOfWeek == day.DayOfWeek);
            if (existing is null)
                row.Days.Add(existing = new WorkScheduleDay { DayOfWeek = (byte)day.DayOfWeek });
            existing.IsWorkingDay = day.IsWorkingDay;
            existing.StartTime = day.StartTime;
            existing.EndTime = day.EndTime;
            existing.GraceMinutes = day.GraceMinutes;
        }
    }

    private static ScheduleDto ToDto(WorkSchedule s)
        => new(s.Id, s.Name, s.Description, s.IsDefault, s.IsActive,
            s.Days.OrderBy(d => d.DayOfWeek).Select(d => new ScheduleDayDto(d.DayOfWeek, d.IsWorkingDay, d.StartTime, d.EndTime, d.GraceMinutes)).ToList(),
            Convert.ToBase64String(s.RowVersion));

    private static SettingsDto ToDto(AttendanceSetting s)
        => new(s.RequireSelfie, s.RequireLocation, s.GeofenceMode, s.MaxAccuracyMeters, s.DebounceSeconds, s.MinInToOutMinutes,
            s.RowVersion.Length == 0 ? null : Convert.ToBase64String(s.RowVersion));

    private static HolidayDto ToDto(Holiday h) => new(h.Id, h.Title, h.HolidayDate, h.Remarks, Convert.ToBase64String(h.RowVersion));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
