using PocoUnlockAssistant.Core.Automation;

namespace PocoUnlockAssistant.Core.Persistence;

public enum ExecutionMode
{
    Monitoring,
    ManualAssisted,
    ControlledAdb,
}

/// <summary>One scheduled attempt, as stored in the audit database.</summary>
public sealed record AttemptRecord
{
    public required Guid AttemptId { get; init; }
    public DateTimeOffset ScheduledBeijing { get; init; }
    public DateTimeOffset ScheduledJakarta { get; init; }
    public string? NtpServer { get; init; }
    public double? NtpOffsetMs { get; init; }
    public double? TimeUncertaintyMs { get; init; }
    public double? NetworkLatencyMs { get; init; }
    public string? DeviceModel { get; init; }
    public string? HyperOsVersion { get; init; }
    public string? AdbStatus { get; init; }
    public string? CommunityVersion { get; init; }
    public ExecutionMode Mode { get; init; } = ExecutionMode.ManualAssisted;
    public string? UiVerification { get; init; }
    public DateTimeOffset? TriggeredAtUtc { get; init; }
    public DateTimeOffset? AdbCompletedAtUtc { get; init; }
    public DateTimeOffset? ResponseObservedAtUtc { get; init; }
    public FinalResult? Result { get; init; }
    public ConfidenceLevel? Confidence { get; init; }
    public SubmissionState State { get; init; } = SubmissionState.Idle;
    public string? ErrorMessage { get; init; }
    public string? ScreenshotPath { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
