using System.Diagnostics;

namespace PocoUnlockAssistant.Core.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Wall-clock source used when available (also detects manual clock changes).</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Countdown source that does not depend on the Windows clock. It holds an anchor
/// (trusted UTC instant) and advances it with the monotonic <see cref="Stopwatch"/>,
/// so a manual clock change or a time-service jump cannot move the countdown.
/// </summary>
public sealed class MonotonicClock : IClock
{
    private readonly object _gate = new();
    private DateTimeOffset _anchorUtc;
    private long _anchorTimestamp;

    public MonotonicClock(DateTimeOffset utcNow)
    {
        _anchorUtc = utcNow;
        _anchorTimestamp = Stopwatch.GetTimestamp();
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _anchorUtc + Stopwatch.GetElapsedTime(_anchorTimestamp);
            }
        }
    }

    /// <summary>Re-anchors to a trusted instant, typically right after an NTP sync or at T-0.</summary>
    public void Reanchor(DateTimeOffset trustedUtc)
    {
        lock (_gate)
        {
            _anchorUtc = trustedUtc;
            _anchorTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>How far the monotonic source has drifted from the OS clock (clock-tamper detection).</summary>
    public TimeSpan SystemClockDrift()
    {
        lock (_gate)
        {
            return (UtcNow - DateTimeOffset.UtcNow).Duration();
        }
    }

    public bool IsSystemClockSuspect(TimeSpan threshold) => SystemClockDrift() > threshold;
}
