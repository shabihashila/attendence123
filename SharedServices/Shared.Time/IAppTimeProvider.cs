namespace Shared.Time;

/// <summary>
/// Application clock. Inject this instead of calling
/// <c>DateTime.UtcNow</c> / <c>DateTimeOffset.UtcNow</c> directly so time is
/// explicit, consistent (always offset-aware), and replaceable in tests.
/// <c>0</c> is the conventional "system" actor id for audit fields.
/// </summary>
public interface IAppTimeProvider
{
    /// <summary>Current instant with offset (server clock). Use for all audit timestamps.</summary>
    DateTimeOffset UtcNowOffset();

    /// <summary>Current UTC wall-clock time.</summary>
    DateTime UtcNow();

    /// <summary>Current server-local wall-clock time. Never persist this; persist <see cref="UtcNowOffset"/>.</summary>
    DateTime LocalNow();

    /// <summary>Current UTC calendar date.</summary>
    DateOnly TodayUtc();
}
