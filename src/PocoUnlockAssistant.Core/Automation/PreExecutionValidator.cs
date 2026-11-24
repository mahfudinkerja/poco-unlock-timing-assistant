namespace PocoUnlockAssistant.Core.Automation;

public sealed record GateInput
{
    public bool TimeSynchronized { get; init; }
    public TimeSpan TimeUncertainty { get; init; }
    public TimeSpan MaxAllowedUncertainty { get; init; } = TimeSpan.FromMilliseconds(150);
    public bool TargetReached { get; init; }
    public bool WindowMissed { get; init; }
    public bool DeviceReady { get; init; }
    public bool ScreenVerified { get; init; }
    public bool ButtonEnabled { get; init; }
    public bool SecurityChallengePresent { get; init; }
    public bool AlreadyExecuted { get; init; }
    public bool EmergencyStopped { get; init; }
}

public sealed record GateCheck(string Name, bool Passed, string Detail);

public sealed record GateResult(bool Passed, IReadOnlyList<GateCheck> Checks)
{
    public IReadOnlyList<GateCheck> Failures => Checks.Where(c => !c.Passed).ToArray();

    public string Summary => Passed
        ? "All pre-execution checks passed."
        : "Blocked: " + string.Join("; ", Failures.Select(f => f.Detail));
}

/// <summary>
/// The final safety gate. Every condition must hold before the single UI action is sent.
/// Any failure means the automation aborts and the operator falls back to manual mode.
/// </summary>
public static class PreExecutionValidator
{
    public static GateResult Evaluate(GateInput input)
    {
        var checks = new List<GateCheck>
        {
            new("time-synced", input.TimeSynchronized,
                input.TimeSynchronized ? "NTP time is synchronized." : "NTP time is not synchronized."),
            new("time-uncertainty", input.TimeUncertainty <= input.MaxAllowedUncertainty,
                $"Estimated uncertainty {input.TimeUncertainty.TotalMilliseconds:0} ms " +
                $"(limit {input.MaxAllowedUncertainty.TotalMilliseconds:0} ms)."),
            new("target-reached", input.TargetReached,
                input.TargetReached ? "00:00:00 Asia/Shanghai reached." : "Target time not reached."),
            new("window-not-missed", !input.WindowMissed,
                input.WindowMissed
                    ? "Target window already passed; late submission is not allowed."
                    : "Within the execution window."),
            new("device-ready", input.DeviceReady,
                input.DeviceReady ? "POCO F7 is connected and authorized." : "Device is not ready."),
            new("screen-verified", input.ScreenVerified,
                input.ScreenVerified ? "Expected Xiaomi Community screen verified." : "Screen not verified."),
            new("button-enabled", input.ButtonEnabled,
                input.ButtonEnabled ? "Submission button is enabled." : "Submission button missing or disabled."),
            new("no-security-challenge", !input.SecurityChallengePresent,
                input.SecurityChallengePresent
                    ? "CAPTCHA or verification challenge detected."
                    : "No security challenge detected."),
            new("no-duplicate", !input.AlreadyExecuted,
                input.AlreadyExecuted
                    ? "This attempt already triggered a submission."
                    : "No submission has been triggered for this attempt."),
            new("not-emergency-stopped", !input.EmergencyStopped,
                input.EmergencyStopped ? "Emergency stop is active." : "Emergency stop is clear."),
        };

        return new GateResult(checks.All(c => c.Passed), checks);
    }
}
