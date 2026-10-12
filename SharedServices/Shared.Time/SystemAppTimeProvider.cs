namespace Shared.Time;

/// <summary>
/// Production clock backed by <see cref="TimeProvider.System"/>. Pass a custom
/// <see cref="TimeProvider"/> (e.g. FakeTimeProvider in tests) to control time.
/// </summary>
public sealed class SystemAppTimeProvider(TimeProvider inner) : IAppTimeProvider
{
    /// <summary>Shared system clock for parameterless model defaults and static helpers.</summary>
    public static IAppTimeProvider System { get; } = new SystemAppTimeProvider(TimeProvider.System);

    public DateTimeOffset UtcNowOffset() => inner.GetUtcNow();

    public DateTime UtcNow() => inner.GetUtcNow().UtcDateTime;

    public DateTime LocalNow() => TimeZoneInfo.ConvertTime(inner.GetUtcNow(), TimeZoneInfo.Local).DateTime;

    public DateOnly TodayUtc() => DateOnly.FromDateTime(inner.GetUtcNow().UtcDateTime);
}
