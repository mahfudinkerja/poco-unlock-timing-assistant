namespace PocoUnlockAssistant.Core.Time;

public enum SyncHealth
{
    /// <summary>No usable measurement.</summary>
    Failed,
    /// <summary>Servers answered but disagree beyond tolerance, or latency is too high.</summary>
    Unreliable,
    /// <summary>Usable but with a wider uncertainty than ideal.</summary>
    Degraded,
    /// <summary>Low uncertainty, consistent responders.</summary>
    Healthy,
}

public sealed record NtpSample(
    string Server,
    TimeSpan Offset,
    TimeSpan RoundTrip,
    int Stratum);

public sealed record TimeSyncReport
{
    public required bool Ok { get; init; }
    public required SyncHealth Health { get; init; }
    public required DateTimeOffset MeasuredAtUtc { get; init; }
    public TimeSpan Offset { get; init; }
    public TimeSpan Uncertainty { get; init; }
    public TimeSpan BestRoundTrip { get; init; }
    public string? Server { get; init; }
    public int Stratum { get; init; }
    public string? FailureReason { get; init; }
    public bool Disagreement { get; init; }
    public IReadOnlyList<NtpSample> Samples { get; init; } = [];

    /// <summary>True when automation may rely on this measurement.</summary>
    public bool IsTrustworthy(TimeSpan maxUncertainty) =>
        Ok && Health is SyncHealth.Healthy or SyncHealth.Degraded && Uncertainty <= maxUncertainty;

    public static TimeSyncReport Failure(string reason, DateTimeOffset measuredAt, IReadOnlyList<NtpSample>? samples = null) =>
        new()
        {
            Ok = false,
            Health = SyncHealth.Failed,
            MeasuredAtUtc = measuredAt,
            FailureReason = reason,
            Samples = samples ?? [],
        };
}

/// <summary>
/// Combines per-server measurements into one consensus offset and an uncertainty estimate.
/// </summary>
public static class NtpConsensus
{
    public static readonly TimeSpan DefaultMaxUncertainty = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultDisagreementTolerance = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan DefaultHealthyUncertainty = TimeSpan.FromMilliseconds(40);

    public static TimeSyncReport Build(
        IReadOnlyList<NtpSample> samples,
        DateTimeOffset measuredAtUtc,
        TimeSpan? disagreementTolerance = null,
        TimeSpan? maxUncertainty = null,
        TimeSpan? healthyUncertainty = null)
    {
        var tolerance = disagreementTolerance ?? DefaultDisagreementTolerance;
        var maxUnc = maxUncertainty ?? DefaultMaxUncertainty;
        var healthyUnc = healthyUncertainty ?? DefaultHealthyUncertainty;

        if (samples.Count == 0)
        {
            return TimeSyncReport.Failure("No NTP server responded.", measuredAtUtc);
        }

        // Median offset is robust against a single lying or badly delayed server.
        var sorted = samples.OrderBy(s => s.Offset).ToArray();
        var median = sorted[sorted.Length / 2].Offset;

        var deviation = sorted.Max(s => (s.Offset - median).Duration());
        var disagreement = deviation > tolerance;

        var best = samples.OrderBy(s => s.RoundTrip).First();
        var spread = (sorted[^1].Offset - sorted[0].Offset).Duration();
        var uncertainty = best.RoundTrip / 2 + spread / 2;

        var health = disagreement || uncertainty > maxUnc
            ? SyncHealth.Unreliable
            : uncertainty <= healthyUnc
                ? SyncHealth.Healthy
                : SyncHealth.Degraded;

        return new TimeSyncReport
        {
            Ok = true,
            Health = health,
            MeasuredAtUtc = measuredAtUtc,
            Offset = median,
            Uncertainty = uncertainty,
            BestRoundTrip = best.RoundTrip,
            Server = best.Server,
            Stratum = best.Stratum,
            Disagreement = disagreement,
            Samples = samples,
            FailureReason = disagreement
                ? $"NTP servers disagree by {deviation.TotalMilliseconds:0} ms (tolerance {tolerance.TotalMilliseconds:0} ms)."
                : uncertainty > maxUnc
                    ? $"Estimated uncertainty {uncertainty.TotalMilliseconds:0} ms exceeds {maxUnc.TotalMilliseconds:0} ms."
                    : null,
        };
    }
}
