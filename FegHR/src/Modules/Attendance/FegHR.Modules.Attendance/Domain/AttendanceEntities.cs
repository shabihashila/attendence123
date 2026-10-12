using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FegHR.Modules.Attendance.Domain;

/// <summary>The vocabulary of the geo-attendance tables. The values appear verbatim in the SQL scripts and the UI.</summary>
internal static class LocationTypes
{
    public const string Office = "Office";
    public const string Site = "Site";
    public const string RemoteAllowed = "RemoteAllowed";
    public static readonly string[] All = [Office, Site, RemoteAllowed];
}

/// <summary>The server's verdict on a punch. Written by <c>attendance.usp_Punch_Record</c>; HR may reclassify it on review.</summary>
internal static class LocationStatuses
{
    public const string InOffice = "InOffice";
    public const string Remote = "Remote";
    public const string OutsideGeofence = "OutsideGeofence";
    public const string Unverified = "Unverified";
    public const string Suspicious = "Suspicious";
    public static readonly string[] All = [InOffice, Remote, OutsideGeofence, Unverified, Suspicious];
    /// <summary>Anything not squarely at an approved location lands in the review queue.</summary>
    public static readonly string[] NeedsReview = [OutsideGeofence, Unverified, Suspicious];
}

internal static class GeofenceModes
{
    public const string Enforce = "Enforce";
    public const string Flag = "Flag";
    public const string Off = "Off";
    public static readonly string[] All = [Enforce, Flag, Off];
}

internal static class LogSources
{
    public const string Manual = "Manual";
}

/// <summary><c>attendance.Settings</c>: the company's punch rules. One row per company.</summary>
internal sealed class AttendanceSetting : TenantEntity
{
    public bool RequireSelfie { get; set; } = true;
    public bool RequireLocation { get; set; } = true;
    public string GeofenceMode { get; set; } = GeofenceModes.Enforce;
    public int MaxAccuracyMeters { get; set; } = 200;
    public int DebounceSeconds { get; set; } = 90;
    public int MinInToOutMinutes { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}

/// <summary><c>attendance.WorkLocations</c>: a geofence a punch is matched against.</summary>
internal sealed class WorkLocation : TenantEntity
{
    public int? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LocationType { get; set; } = LocationTypes.Office;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int RadiusMeters { get; set; } = 150;
    public string? AllowedPublicIps { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>attendance.WorkSchedules</c> with one <see cref="WorkScheduleDay"/> per weekday.</summary>
internal sealed class WorkSchedule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public List<WorkScheduleDay> Days { get; set; } = [];
}

/// <summary><c>attendance.WorkScheduleDays</c>. <see cref="DayOfWeek"/>: 0 = Sunday ... 6 = Saturday.</summary>
internal sealed class WorkScheduleDay : TenantEntity
{
    public int WorkScheduleId { get; set; }
    public byte DayOfWeek { get; set; }
    public bool IsWorkingDay { get; set; } = true;
    public TimeOnly StartTime { get; set; } = new(9, 0);
    public TimeOnly EndTime { get; set; } = new(18, 0);
    public int GraceMinutes { get; set; } = 15;
    public bool IsActive { get; set; } = true;
}

/// <summary><c>attendance.EmployeeSchedules</c>: an employee's own schedule for a period.</summary>
internal sealed class EmployeeSchedule : TenantEntity
{
    public int EmployeeId { get; set; }
    public int WorkScheduleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>attendance.Holidays</c>.</summary>
internal sealed class Holiday : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public DateOnly HolidayDate { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary><c>attendance.EmployeeDevices</c>: the handsets an employee punches from.</summary>
internal sealed class EmployeeDevice : TenantEntity
{
    public int EmployeeId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Platform { get; set; }
    public bool IsApproved { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// <c>attendance.Punches</c>: an append-only punch event. Inserted only by
/// <c>attendance.usp_Punch_Record</c>; here it is read, and the review columns are updated.
/// The captured observations (coordinates, accuracy, IP, photo) are never edited.
/// </summary>
internal sealed class Punch : ITenantOwned, IAuditable, IVersioned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int EmployeeId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public bool? IsMockLocation { get; set; }
    public string? DeviceId { get; set; }
    public string? Platform { get; set; }
    public string? SelfieKey { get; set; }
    public string? Remarks { get; set; }
    public DateTimeOffset PunchedAt { get; set; }
    public DateOnly LogDate { get; set; }
    public TimeOnly LocalTime { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? MatchedLocationId { get; set; }
    public double? DistanceMeters { get; set; }
    public string LocationStatus { get; set; } = string.Empty;
    public string FlagsJson { get; set; } = "[]";
    public bool IsReviewed { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary><c>attendance.DailyLogs</c>: one row per employee and date, derived from the punches or keyed in by HR.</summary>
internal sealed class DailyLog : ITenantOwned, IAuditable, IVersioned
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly LogDate { get; set; }
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class AttendanceSettingConfiguration : IEntityTypeConfiguration<AttendanceSetting>
{
    public void Configure(EntityTypeBuilder<AttendanceSetting> b)
    {
        b.ToTable("Settings");
        b.Property(x => x.GeofenceMode).HasMaxLength(10);
    }
}

internal sealed class WorkLocationConfiguration : IEntityTypeConfiguration<WorkLocation>
{
    public void Configure(EntityTypeBuilder<WorkLocation> b)
    {
        b.ToTable("WorkLocations");
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.LocationType).HasMaxLength(30);
        b.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
        b.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
        b.Property(x => x.AllowedPublicIps).HasMaxLength(500);
        b.Property(x => x.Address).HasMaxLength(500);
    }
}

internal sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable("WorkSchedules");
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasMany(x => x.Days).WithOne().HasForeignKey(x => x.WorkScheduleId);
    }
}

internal sealed class WorkScheduleDayConfiguration : IEntityTypeConfiguration<WorkScheduleDay>
{
    public void Configure(EntityTypeBuilder<WorkScheduleDay> b)
    {
        b.ToTable("WorkScheduleDays");
        b.Property(x => x.StartTime).HasColumnType("time(0)");
        b.Property(x => x.EndTime).HasColumnType("time(0)");
    }
}

internal sealed class EmployeeScheduleConfiguration : IEntityTypeConfiguration<EmployeeSchedule>
{
    public void Configure(EntityTypeBuilder<EmployeeSchedule> b)
    {
        b.ToTable("EmployeeSchedules");
        b.Property(x => x.Remarks).HasMaxLength(500);
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("Holidays");
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Remarks).HasMaxLength(500);
    }
}

internal sealed class EmployeeDeviceConfiguration : IEntityTypeConfiguration<EmployeeDevice>
{
    public void Configure(EntityTypeBuilder<EmployeeDevice> b)
    {
        b.ToTable("EmployeeDevices");
        b.Property(x => x.DeviceId).HasMaxLength(100);
        b.Property(x => x.Label).HasMaxLength(150);
        b.Property(x => x.Platform).HasMaxLength(30);
    }
}

internal sealed class PunchConfiguration : IEntityTypeConfiguration<Punch>
{
    public void Configure(EntityTypeBuilder<Punch> b)
    {
        b.ToTable("Punches");
        b.Property(x => x.Latitude).HasColumnType("decimal(9,6)");
        b.Property(x => x.Longitude).HasColumnType("decimal(9,6)");
        b.Property(x => x.LocalTime).HasColumnType("time(0)");
        b.Property(x => x.ReviewNote).HasMaxLength(500);
        b.Property(x => x.LocationStatus).HasMaxLength(30);
    }
}

internal sealed class DailyLogConfiguration : IEntityTypeConfiguration<DailyLog>
{
    public void Configure(EntityTypeBuilder<DailyLog> b)
    {
        b.ToTable("DailyLogs");
        b.Property(x => x.InTime).HasColumnType("time(0)");
        b.Property(x => x.OutTime).HasColumnType("time(0)");
        b.Property(x => x.Source).HasMaxLength(30);
        b.Property(x => x.Remarks).HasMaxLength(500);
    }
}
