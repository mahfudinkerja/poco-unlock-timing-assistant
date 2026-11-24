using PocoUnlockAssistant.Core.Time;
using Xunit;

namespace PocoUnlockAssistant.Tests;

public class TimeTests
{
    [Fact]
    public void BeijingMidnight_IsPreviousDay2300Wib()
    {
        var beijingMidnight = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8));

        var wib = TargetTimeCalculator.ToJakarta(beijingMidnight);

        Assert.Equal(23, wib.Hour);
        Assert.Equal(0, wib.Minute);
        Assert.Equal(25, wib.Day);
        Assert.Equal(11, wib.Month);
        Assert.Equal(TimeSpan.FromHours(7), wib.Offset);
        Assert.Equal(beijingMidnight.UtcDateTime, wib.UtcDateTime);
    }

    [Fact]
    public void BeijingNoon_Is1100Wib()
    {
        var beijingNoon = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(8));

        var wib = TargetTimeCalculator.ToJakarta(beijingNoon);

        Assert.Equal(11, wib.Hour);
        Assert.Equal(26, wib.Day);
    }

    [Fact]
    public void WitaAndWit_UseTheirOwnOffsets()
    {
        var instant = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(TimeSpan.FromHours(8), ZoneCatalog.ResolveOffset(ZoneCatalog.WitaId, instant));
        Assert.Equal(TimeSpan.FromHours(9), ZoneCatalog.ResolveOffset(ZoneCatalog.WitId, instant));
        Assert.Equal(TimeSpan.FromHours(8), ZoneCatalog.ResolveOffset(ZoneCatalog.BeijingId, instant));
        Assert.Equal(TimeSpan.FromHours(7), ZoneCatalog.ResolveOffset(ZoneCatalog.JakartaId, instant));
    }

    [Fact]
    public void BeijingTargetFromJakartaDate_LandsOnNextBeijingDay()
    {
        var target = TargetTimeCalculator.BeijingTargetFromJakartaDate(new DateOnly(2026, 11, 25));

        Assert.Equal(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8)), target);
        Assert.Equal(23, TargetTimeCalculator.ToJakarta(target).Hour);
    }

    [Fact]
    public void NextOccurrence_IsStrictlyInTheFuture_AndRollsOverMidnight()
    {
        var justBefore = new DateTimeOffset(2026, 11, 25, 23, 59, 59, TimeSpan.FromHours(8));
        var atTarget = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8));
        var afterTarget = atTarget.AddSeconds(1);

        Assert.Equal(atTarget, TargetTimeCalculator.NextOccurrence(justBefore, TimeSpan.FromHours(8), TimeOnly.MinValue));
        Assert.Equal(atTarget.AddDays(1), TargetTimeCalculator.NextOccurrence(atTarget, TimeSpan.FromHours(8), TimeOnly.MinValue));
        Assert.Equal(atTarget.AddDays(1), TargetTimeCalculator.NextOccurrence(afterTarget, TimeSpan.FromHours(8), TimeOnly.MinValue));
    }

    [Fact]
    public void NextBeijingReset_UsesShanghaiOffset()
    {
        var now = new DateTimeOffset(2026, 11, 25, 16, 30, 0, TimeSpan.Zero); // 00:30 Beijing, past reset

        var next = TargetTimeCalculator.NextBeijingReset(now);

        Assert.Equal(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.FromHours(8)), next);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 23, 0, 0, TimeSpan.FromHours(7)),
            TargetTimeCalculator.ToJakarta(next));
    }

    [Fact]
    public void TriggerWindow_AndMissedDetection()
    {
        var target = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8));
        var grace = TimeSpan.FromSeconds(3);

        Assert.True(TargetTimeCalculator.IsWithinTriggerWindow(target, target, grace));
        Assert.True(TargetTimeCalculator.IsWithinTriggerWindow(target, target.AddSeconds(1), grace));
        Assert.False(TargetTimeCalculator.IsWithinTriggerWindow(target, target.AddSeconds(5), grace));
        Assert.True(TargetTimeCalculator.IsMissed(target, target.AddSeconds(5), grace));
        Assert.False(TargetTimeCalculator.IsMissed(target, target.AddMilliseconds(200), grace));
    }

    [Fact]
    public void CountdownFormatting_IsMillisecondPrecise()
    {
        var remaining = new TimeSpan(1, 4, 12, 9, 318);

        Assert.Equal("01d 04:12:09.318", TargetTimeCalculator.FormatCountdown(remaining));
        Assert.Equal("00d 00:00:00.000", TargetTimeCalculator.FormatCountdown(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void NtpConsensus_HealthyWhenServersAgree()
    {
        var samples = new[]
        {
            new NtpSample("a", TimeSpan.FromMilliseconds(12), TimeSpan.FromMilliseconds(40), 2),
            new NtpSample("b", TimeSpan.FromMilliseconds(14), TimeSpan.FromMilliseconds(30), 2),
            new NtpSample("c", TimeSpan.FromMilliseconds(11), TimeSpan.FromMilliseconds(55), 3),
        };

        var report = NtpConsensus.Build(samples, DateTimeOffset.UtcNow);

        Assert.True(report.Ok);
        Assert.False(report.Disagreement);
        Assert.Equal(SyncHealth.Healthy, report.Health);
        Assert.True(report.IsTrustworthy(TimeSpan.FromMilliseconds(150)));
        Assert.Equal("b", report.Server);
    }

    [Fact]
    public void NtpConsensus_FailsWithNoResponders()
    {
        var report = NtpConsensus.Build([], DateTimeOffset.UtcNow);

        Assert.False(report.Ok);
        Assert.Equal(SyncHealth.Failed, report.Health);
        Assert.False(report.IsTrustworthy(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void NtpConsensus_FlagsDisagreementAndBlocksAutomation()
    {
        var samples = new[]
        {
            new NtpSample("a", TimeSpan.FromMilliseconds(0), TimeSpan.FromMilliseconds(20), 2),
            new NtpSample("b", TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(25), 2),
        };

        var report = NtpConsensus.Build(samples, DateTimeOffset.UtcNow);

        Assert.True(report.Ok);
        Assert.True(report.Disagreement);
        Assert.Equal(SyncHealth.Unreliable, report.Health);
        Assert.False(report.IsTrustworthy(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void NtpConsensus_UncertaintyIncludesRoundTripLatency()
    {
        var samples = new[]
        {
            new NtpSample("slow", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(400), 2),
        };

        var report = NtpConsensus.Build(samples, DateTimeOffset.UtcNow);

        Assert.True(report.Uncertainty >= TimeSpan.FromMilliseconds(200));
        Assert.False(report.IsTrustworthy(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void MonotonicClock_AdvancesIndependentlyOfTheWallClock()
    {
        var anchor = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
        var clock = new MonotonicClock(anchor);

        Thread.Sleep(60);
        var first = clock.UtcNow;

        clock.Reanchor(anchor.AddHours(2)); // simulates an NTP correction / manual clock change
        var second = clock.UtcNow;

        Assert.True(first > anchor);
        Assert.True(second >= anchor.AddHours(2));
        Assert.True(second - first >= TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MonotonicClock_DetectsLargeSystemClockDrift()
    {
        var clock = new MonotonicClock(DateTimeOffset.UtcNow.AddHours(-3)); // deliberately wrong anchor

        Assert.True(clock.SystemClockDrift() > TimeSpan.FromMinutes(60));
        Assert.True(clock.IsSystemClockSuspect(TimeSpan.FromSeconds(30)));
    }
}
