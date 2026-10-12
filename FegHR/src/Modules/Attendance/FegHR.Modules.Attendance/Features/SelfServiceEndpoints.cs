using System.Globalization;
using System.Text.Json;
using FegHR.Modules.Attendance.Domain;
using FegHR.Modules.Attendance.Persistence;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using FegHR.SharedServices.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Shared.Time;

namespace FegHR.Modules.Attendance.Features;

/// <summary>Permission codes seeded by <c>SeedData/01_Metadata.sql</c>.</summary>
internal static class AttendancePermissions
{
    public const string PunchSelf = "attendance.punch.self";
    public const string LogsRead = "attendance.logs.read";
    public const string LogsWrite = "attendance.logs.write";
    public const string Review = "attendance.review";
    public const string SetupRead = "attendance.setup.read";
    public const string SetupWrite = "attendance.setup.write";
    public const string ReportsRead = "attendance.reports.read";
}

internal sealed record PunchDto(long Id, DateTimeOffset PunchedAt, string LocalTime, string Direction, string LocationStatus,
    string? LocationName, double? DistanceMeters, IReadOnlyList<string> Flags, bool HasPhoto, bool NeedsReview);

internal sealed record PunchResponse(PunchDto Punch, bool WasDuplicate, string Message);

internal sealed record FenceDto(int Id, string Name, string LocationType, decimal? Latitude, decimal? Longitude, int RadiusMeters);

internal sealed record PunchRules(bool RequireSelfie, bool RequireLocation, string GeofenceMode, int MaxAccuracyMeters);

/// <summary>Everything the punch screen needs: today so far, the shift, the rules and the fences.</summary>
internal sealed record MyTodayDto(
    string Date, string EmployeeName, string EmployeeCode, string NextDirection, string? InTime, string? OutTime,
    bool IsWorkingDay, string? ShiftStart, string? ShiftEnd, string? ScheduleName, string? Holiday,
    PunchRules Rules, IReadOnlyList<FenceDto> Locations, IReadOnlyList<PunchDto> Punches);

/// <summary>
/// Self-service attendance: the signed-in employee records their own punch with their location
/// and a photo taken at that moment. The employee is always resolved from the session, so nobody
/// can punch for anybody else; the direction, the geofence verdict and the timestamp are decided
/// by the server.
/// </summary>
internal static class SelfServiceEndpoints
{
    private const long MaxPhotoBytes = 2 * 1024 * 1024;
    private const string NotLinked = "Your sign-in is not linked to an employee record. Ask HR to link it before recording attendance.";
    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(SelfServiceEndpoints).FullName);

    public static void Map(RouteGroupBuilder group)
    {
        var my = group.MapGroup("/my").RequirePermission(AttendancePermissions.PunchSelf);

        my.MapGet("/today", async (ITenantContext tenant, IEmployeeDirectory employees, ITenantDirectory tenants,
                AttendanceDbContext db, AttendanceProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);

                var zone = await tenants.GetClockAsync(ct);
                var now = clock.UtcNowOffset();
                var today = zone.DateOf(now);
                var punches = await db.Punches.AsNoTracking().Where(p => p.EmployeeId == me.Id && p.LogDate == today && p.IsActive)
                    .OrderBy(p => p.PunchedAt).ToListAsync(ct);
                var names = await LocationNamesAsync(db, punches, ct);
                var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AttendanceSetting();
                var day = (await procedures.EmployeeDaysAsync(me.Id, today, today, today, ct)).FirstOrDefault();
                var fences = await db.WorkLocations.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name)
                    .Select(w => new FenceDto(w.Id, w.Name, w.LocationType, w.Latitude, w.Longitude, w.RadiusMeters)).ToListAsync(ct);
                var schedule = await procedures.ScheduleForDateAsync(me.Id, today, ct);

                return ApiResults.Ok(new MyTodayDto(
                    today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), me.FullName, me.Code,
                    NextDirection(punches, now, settings.MinInToOutMinutes),
                    Time(day?.InTime), Time(day?.OutTime),
                    schedule?.IsWorkingDay ?? true, Time(day?.ShiftStart), Time(day?.ShiftEnd),
                    schedule?.ScheduleName, day?.Status == "Holiday" ? day.Note : null,
                    new PunchRules(settings.RequireSelfie, settings.RequireLocation, settings.GeofenceMode, settings.MaxAccuracyMeters),
                    fences, punches.Select(p => ToDto(p, names)).ToList()));
            })
            .WithName("GetMyAttendanceToday");

        // One request carries the observation and the photo, so a punch is never half-recorded.
        my.MapPost("/punch", async (HttpRequest request, ITenantContext tenant, IEmployeeDirectory employees,
                ITenantDirectory tenants, AttendanceDbContext db, AttendanceProcedures procedures, IFileStorage storage,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return ApiResults.Fail("Send the punch as form data.");
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);

                var form = await request.ReadFormAsync(ct);
                var latitude = Coordinate(form["latitude"], 90);
                var longitude = Coordinate(form["longitude"], 180);
                if (latitude is null || longitude is null)
                    latitude = longitude = null;

                var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AttendanceSetting();
                var photo = form.Files.GetFile("photo");
                if (settings.RequireSelfie && photo is null)
                    return ApiResults.Fail("A photo is required to record attendance.", StatusCodes.Status422UnprocessableEntity);
                if (settings.RequireLocation && latitude is null)
                    return ApiResults.Fail("Your location is required to record attendance. Allow location access and try again.",
                        StatusCodes.Status422UnprocessableEntity);

                var zone = await tenants.GetClockAsync(ct);
                var now = clock.UtcNowOffset();
                var local = zone.LocalOf(now);

                string? selfieKey = null;
                if (photo is not null)
                {
                    if (photo.Length == 0 || photo.Length > MaxPhotoBytes)
                        return ApiResults.Fail("The photo must be 2 MB or smaller.");
                    using var buffer = new MemoryStream();
                    await photo.CopyToAsync(buffer, ct);
                    if (ImageFiles.Detect(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) is not { } type)
                        return ApiResults.Fail("The photo must be a JPEG, PNG or WebP image.");
                    buffer.Position = 0;
                    // Per-punch evidence: a new key every time, never overwritten.
                    var stored = await storage.UploadAsync(
                        $"punches/{me.Id}/{local:yyyyMMdd}/{local:HHmmss}-{Guid.NewGuid():N}{type.Extension}", buffer, type.ContentType, ct);
                    if (!stored.Success)
                    {
                        Logger.Warning("Punch photo upload failed for employee {EmployeeId}: {Error}", me.Id, stored.Error);
                        return ApiResults.Fail("The photo could not be stored. Please try again in a moment.", StatusCodes.Status502BadGateway);
                    }
                    selfieKey = stored.Key;
                }

                PunchResult result;
                try
                {
                    result = await procedures.RecordPunchAsync(new PunchCommand(
                        me.Id, latitude, longitude, Number(form["accuracyMeters"]), Flag(form["isMockLocation"]),
                        Text(form["deviceId"], 100), Text(form["deviceLabel"], 150), Text(form["platform"], 30) ?? "Web",
                        DateTimeOffset.TryParse(form["clientPunchedAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var claimed) ? claimed : null,
                        selfieKey, Text(form["remarks"], 500), request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                        now, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), tenant.UserId.Value), ct);
                }
                catch
                {
                    // Refused (outside the geofence, simulated location, ...): the photo is evidence of nothing.
                    if (selfieKey is not null)
                        await storage.DeleteAsync(selfieKey, ct);
                    throw;
                }
                // A retry within the debounce window returns the first punch; this request's photo is surplus.
                if (result.WasDuplicate && selfieKey is not null && result.SelfieKey != selfieKey)
                    await storage.DeleteAsync(selfieKey, ct);

                var dto = new PunchDto(result.Id, result.PunchedAt, result.LocalTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    result.Direction, result.LocationStatus, result.LocationName, result.DistanceMeters, Flags(result.FlagsJson),
                    result.SelfieKey is not null, LocationStatuses.NeedsReview.Contains(result.LocationStatus));
                var message = result.WasDuplicate ? "Already recorded a moment ago." : Describe(result);
                return ApiResults.Ok(new PunchResponse(dto, result.WasDuplicate, message), message);
            })
            .DisableAntiforgery()
            .WithName("RecordMyPunch");

        my.MapGet("/days", async (DateOnly? from, DateOnly? to, ITenantContext tenant, IEmployeeDirectory employees,
                ITenantDirectory tenants, AttendanceProcedures procedures, IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (await employees.GetByUserAsync(tenant.UserId!.Value, ct) is not { } me)
                    return ApiResults.Fail(NotLinked, StatusCodes.Status409Conflict);
                var today = (await tenants.GetClockAsync(ct)).DateOf(clock.UtcNowOffset());
                var start = from ?? new DateOnly(today.Year, today.Month, 1);
                var end = to ?? start.AddMonths(1).AddDays(-1);
                if (end < start)
                    return ApiResults.Fail("The end date is before the start date.");
                return ApiResults.Ok(await procedures.EmployeeDaysAsync(me.Id, start, end, today, ct));
            })
            .WithName("GetMyAttendanceDays");

        // The photo of a punch: the employee's own, or anyone's for a reviewer.
        group.MapGet("/punches/{id:long}/photo", async (long id, ITenantContext tenant, IEmployeeDirectory employees,
                IPermissionProvider permissions, AttendanceDbContext db, IFileStorage storage, CancellationToken ct) =>
            {
                var punch = await db.Punches.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
                if (punch?.SelfieKey is null)
                    return ApiResults.NotFound("The photo");
                var granted = await permissions.GetPermissionsAsync(tenant.RequiredTenantId, tenant.UserId!.Value, ct);
                if (!granted.Contains(AttendancePermissions.Review) && !granted.Contains(AttendancePermissions.LogsRead)
                    && (await employees.GetByUserAsync(tenant.UserId.Value, ct))?.Id != punch.EmployeeId)
                    return ApiResults.Fail("You do not have permission to do this.", StatusCodes.Status403Forbidden);
                return await storage.GetViewUrlAsync(punch.SelfieKey, 10, ct) is { } view
                    ? ApiResults.Ok(new { view.Url, view.ExpiresAt })
                    : ApiResults.Fail("The photo is not available right now.", StatusCodes.Status502BadGateway);
            })
            .RequireStaff()
            .WithName("GetPunchPhoto");
    }

    internal static async Task<Dictionary<int, string>> LocationNamesAsync(AttendanceDbContext db, IReadOnlyList<Punch> punches,
        CancellationToken ct)
    {
        var ids = punches.Where(p => p.MatchedLocationId is not null).Select(p => p.MatchedLocationId!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.WorkLocations.AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
    }

    internal static PunchDto ToDto(Punch p, IReadOnlyDictionary<int, string> names)
        => new(p.Id, p.PunchedAt, p.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture), p.Direction, p.LocationStatus,
            p.MatchedLocationId is { } id ? names.GetValueOrDefault(id) : null, p.DistanceMeters, Flags(p.FlagsJson),
            p.SelfieKey is not null, !p.IsReviewed && LocationStatuses.NeedsReview.Contains(p.LocationStatus));

    internal static IReadOnlyList<string> Flags(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? []; }
        catch (JsonException) { return []; }
    }

    // Mirrors attendance.usp_Punch_Record: the first punch is In; a punch too soon after an In
    // repeats it, so reopening the page cannot close the shift by accident.
    private static string NextDirection(IReadOnlyList<Punch> today, DateTimeOffset now, int minInToOutMinutes)
    {
        if (today.Count == 0)
            return "In";
        var last = today[^1];
        if (last.Direction == "In" && now - last.PunchedAt < TimeSpan.FromMinutes(minInToOutMinutes))
            return "In";
        return last.Direction == "In" ? "Out" : "In";
    }

    // The employee should learn at once that a punch looked odd, not in a payroll dispute.
    private static string Describe(PunchResult r)
    {
        var what = r.Direction == "In" ? "Checked in" : "Checked out";
        var away = r.DistanceMeters switch
        {
            null => null,
            < 1000 => $"{r.DistanceMeters.Value:0} m",
            _ => $"{r.DistanceMeters.Value / 1000:0.0} km"
        };
        return r.LocationStatus switch
        {
            LocationStatuses.InOffice => r.LocationName is null ? $"{what}." : $"{what} at {r.LocationName}.",
            LocationStatuses.Remote => $"{what} remotely ({r.LocationName}).",
            LocationStatuses.OutsideGeofence => away is null || r.LocationName is null
                ? $"{what}, but outside every work location. HR will review it."
                : $"{what}, but {away} from {r.LocationName}. HR will review it.",
            LocationStatuses.Suspicious => $"{what}. The location reported by this device looks altered, so HR will review it.",
            _ => $"{what}. Your location could not be confirmed, so HR will review it."
        };
    }

    private static decimal? Coordinate(string? value, decimal limit)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && Math.Abs(parsed) <= limit
            ? Math.Round(parsed, 6) : null;

    private static double? Number(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed : null;

    private static bool? Flag(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;

    private static string? Text(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static string? Time(TimeSpan? value) => value?.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
