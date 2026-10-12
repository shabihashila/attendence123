namespace FegHR.Modules.Platform.Contracts;

/// <summary>The bound company as other modules and the UI see it.</summary>
public sealed record TenantSummary(int Id, string Slug, string Name, string CurrencyCode, string TimeZoneId);

/// <summary>A branch of the bound company.</summary>
public sealed record BranchSummary(int Id, string Code, string Name, bool IsActive);

/// <summary>
/// The company's wall clock. Attendance dates and times are the company's local date and time,
/// never the server's or UTC's: a 00:30 punch in Dhaka belongs to that Dhaka calendar day.
/// </summary>
public sealed record TenantClock(string TimeZoneId)
{
    private static readonly TimeZoneInfo Fallback = TimeZoneInfo.CreateCustomTimeZone("BDT", TimeSpan.FromHours(6), "BDT", "BDT");

    public TimeZoneInfo Zone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
            catch (TimeZoneNotFoundException) { return Fallback; }
            catch (InvalidTimeZoneException) { return Fallback; }
        }
    }

    /// <summary><paramref name="instant"/> on the company's wall clock.</summary>
    public DateTime LocalOf(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>The company's calendar date at <paramref name="instant"/>.</summary>
    public DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(LocalOf(instant));
}

/// <summary>The licence of the bound company at a moment in time.</summary>
public sealed record LicenseStatus(
    int TenantId,
    string TenantStatus,
    bool IsLicensed,
    string? Kind,
    string? PlanCode,
    string? PlanName,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt)
{
    /// <summary>Whole days left, rounded up; null when the licence has no end.</summary>
    public int? DaysLeft(DateTimeOffset now)
        => IsLicensed && EndsAt is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalDays)) : null;
}

/// <summary>
/// Read access to the bound company's platform data. Every member works on the
/// company bound to the current scope; there is no way to ask about another one.
/// </summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Feature codes of the licence in force; empty when the company is unlicensed.</summary>
    Task<IReadOnlyList<string>> GetFeatureCodesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(bool activeOnly = true, CancellationToken ct = default);

    Task<TenantClock> GetClockAsync(CancellationToken ct = default);

    Task<LicenseStatus?> GetLicenseStatusAsync(CancellationToken ct = default);
}
