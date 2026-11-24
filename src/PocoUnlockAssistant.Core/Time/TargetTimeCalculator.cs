namespace PocoUnlockAssistant.Core.Time;

/// <summary>
/// Converts the Beijing quota-reset time (00:00:00 Asia/Shanghai) into the other
/// zones shown on the dashboard and computes the next occurrence of a target.
/// </summary>
public static class TargetTimeCalculator
{
    public static readonly TimeOnly Midnight = new(0, 0, 0);

    /// <summary>
    /// The next time-of-day occurrence in the given zone, strictly after <paramref name="nowUtc"/>.
    /// </summary>
    public static DateTimeOffset NextOccurrence(DateTimeOffset nowUtc, TimeSpan zoneOffset, TimeOnly timeOfDay)
    {
        var localNow = nowUtc.ToOffset(zoneOffset);
        var candidate = new DateTimeOffset(
            localNow.Year, localNow.Month, localNow.Day,
            timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second,
            zoneOffset);

        if (candidate <= nowUtc)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    /// <summary>Next 00:00:00 Asia/Shanghai (the official quota reset).</summary>
    public static DateTimeOffset NextBeijingReset(DateTimeOffset nowUtc) =>
        NextOccurrence(nowUtc, ZoneCatalog.Beijing(nowUtc), Midnight);

    /// <summary>Next 00:00:00 Asia/Jakarta.</summary>
    public static DateTimeOffset NextJakartaMidnight(DateTimeOffset nowUtc) =>
        NextOccurrence(nowUtc, ZoneCatalog.Jakarta(nowUtc), Midnight);

    /// <summary>
    /// Same instant expressed as wall-clock time in Jakarta (23:00 WIB on the previous
    /// calendar day for a 00:00 Beijing target).
    /// </summary>
    public static DateTimeOffset ToJakarta(DateTimeOffset instant) =>
        instant.ToOffset(ZoneCatalog.Jakarta(instant));

    public static DateTimeOffset ToBeijing(DateTimeOffset instant) =>
        instant.ToOffset(ZoneCatalog.Beijing(instant));

    /// <summary>
    /// Builds the Beijing target for the Jakarta calendar date that precedes it.
    /// Used by the QA checks in the specification ("00:00 Beijing = 23:00 WIB previous day").
    /// </summary>
    public static DateTimeOffset BeijingTargetFromJakartaDate(DateOnly jakartaDate)
    {
        var beijingWall = jakartaDate.ToDateTime(new TimeOnly(23, 0)) + TimeSpan.FromHours(1);
        return new DateTimeOffset(beijingWall, ZoneCatalog.BeijingOffset);
    }

    public static TimeSpan Remaining(DateTimeOffset target, DateTimeOffset now) =>
        target - now;

    /// <summary>
    /// True when the target moment is close enough to act on. <paramref name="lateGrace"/>
    /// bounds how late a trigger may still fire; beyond that a missed window must not run.
    /// </summary>
    public static bool IsWithinTriggerWindow(DateTimeOffset target, DateTimeOffset now, TimeSpan lateGrace) =>
        now >= target - TimeSpan.FromMilliseconds(250) && now <= target + lateGrace;

    public static bool IsMissed(DateTimeOffset target, DateTimeOffset now, TimeSpan lateGrace) =>
        now > target + lateGrace;

    public static string FormatWib(DateTimeOffset instant)
    {
        var wib = ToJakarta(instant);
        return $"{wib:yyyy-MM-dd HH:mm:ss} WIB";
    }

    public static string FormatBeijing(DateTimeOffset instant)
    {
        var cst = ToBeijing(instant);
        return $"{cst:yyyy-MM-dd HH:mm:ss} CST";
    }

    /// <summary>Formats a countdown with millisecond precision, e.g. "00d 04:12:09.318".</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var days = (int)remaining.TotalDays;
        return $"{days:00}d {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}.{remaining.Milliseconds:000}";
    }
}
