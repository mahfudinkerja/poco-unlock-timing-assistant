using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Configuration;
using PocoUnlockAssistant.Core.Device;
using PocoUnlockAssistant.Core.Persistence;
using PocoUnlockAssistant.Core.Time;

namespace PocoUnlockAssistant.Core.Scheduling;

public sealed record PreparationStep(string Name, bool Ok, string Detail);

public sealed record PreparationReport(bool Ready, IReadOnlyList<PreparationStep> Steps)
{
    public string Summary => Ready
        ? "Device, time and Xiaomi Community are ready."
        : "Not ready: " + string.Join("; ", Steps.Where(s => !s.Ok).Select(s => s.Detail));
}

public sealed record ExecutionOutcome(
    Classification Classification,
    AttemptRecord Record,
    GateResult? Gate,
    bool ActionSent);

public sealed record ScreenInspection(
    ScreenMatch Match,
    int NodeCount,
    string? ButtonText,
    string? ScreenshotPath);

/// <summary>
/// Orchestrates one scheduled attempt: synchronization, readiness, arming, monotonic
/// countdown, the single guarded tap at 00:00:00 Asia/Shanghai, and result classification.
/// It contains no UI code and no adb command strings, so the flow is unit-testable.
/// </summary>
public sealed class SubmissionCoordinator
{
    private readonly INtpClient _ntp;
    private readonly IAdbClient _adb;
    private readonly IScreenInspector _screen;
    private readonly IAttemptStore _store;
    private readonly ScreenRecognitionProfile _profile;

    private TimeSyncReport _lastSync = TimeSyncReport.Failure("Not synchronized yet.", DateTimeOffset.UtcNow);
    private AdbDeviceInfo _lastDevice = AdbDeviceInfo.Unavailable("Not probed yet.");
    private string? _communityVersion;
    private bool _emergencyStopped;
    private Guid _attemptId = Guid.NewGuid();

    public SubmissionCoordinator(
        INtpClient ntp,
        IAdbClient adb,
        IScreenInspector screen,
        IAttemptStore store,
        AssistantSettings settings,
        ScreenRecognitionProfile profile,
        MonotonicClock clock)
    {
        _ntp = ntp;
        _adb = adb;
        _screen = screen;
        _store = store;
        Settings = settings;
        _profile = profile;
        Clock = clock;
    }

    public AssistantSettings Settings { get; set; }

    public MonotonicClock Clock { get; }

    public SubmissionStateMachine Machine { get; } = new();

    public TimeSyncReport LastSync => _lastSync;

    public AdbDeviceInfo LastDevice => _lastDevice;

    public string? CommunityVersion => _communityVersion;

    public bool EmergencyStopped => _emergencyStopped;

    public DateTimeOffset Now => Clock.UtcNow;

    /// <summary>Test seam: pins the target instant so the whole flow can be exercised quickly.</summary>
    public DateTimeOffset? TargetOverride { get; set; }

    public DateTimeOffset Target =>
        TargetOverride ?? TargetTimeCalculator.NextOccurrence(Now, ZoneCatalog.Beijing(Now), Settings.TargetTime);

    public TimeSpan Remaining => TargetTimeCalculator.Remaining(Target, Now);

    public event Action<string>? StatusChanged;

    public void EmergencyStop()
    {
        _emergencyStopped = true;
        StatusChanged?.Invoke("EMERGENCY STOP engaged. Automation will not submit.");
    }

    public void ClearEmergencyStop()
    {
        _emergencyStopped = false;
    }

    private void SetState(SubmissionState next, string? reason = null)
    {
        if (Machine.TryTransition(next, Now, out var error, reason))
        {
            StatusChanged?.Invoke($"{next}{(reason is null ? string.Empty : $" — {reason}")}");
        }
        else
        {
            StatusChanged?.Invoke($"Transition blocked: {error}");
        }
    }

    public async Task<TimeSyncReport> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        _lastSync = await _ntp.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        if (_lastSync.Ok)
        {
            // Anchor the monotonic countdown to the consensus NTP instant.
            Clock.Reanchor(_lastSync.MeasuredAtUtc + _lastSync.Offset);
        }

        StatusChanged?.Invoke(_lastSync.Ok
            ? $"NTP {_lastSync.Server}: offset {_lastSync.Offset.TotalMilliseconds:0} ms, uncertainty {_lastSync.Uncertainty.TotalMilliseconds:0} ms"
            : $"NTP failed: {_lastSync.FailureReason}");
        return _lastSync;
    }

    public async Task<AdbDeviceInfo> ProbeDeviceAsync(CancellationToken cancellationToken = default)
    {
        _lastDevice = await _adb.ProbeAsync(cancellationToken).ConfigureAwait(false);
        StatusChanged?.Invoke($"Device: {_lastDevice.StateLabel} ({_lastDevice.DisplayModel})");
        return _lastDevice;
    }

    public async Task<string?> CheckCommunityAsync(CancellationToken cancellationToken = default)
    {
        var installed = await _adb.IsInstalledAsync(Settings.CommunityPackageId, cancellationToken).ConfigureAwait(false);
        _communityVersion = installed
            ? await _adb.GetPackageVersionAsync(Settings.CommunityPackageId, cancellationToken).ConfigureAwait(false) ?? "installed"
            : null;
        StatusChanged?.Invoke(installed
            ? $"Xiaomi Community detected (version {_communityVersion})."
            : $"Xiaomi Community ({Settings.CommunityPackageId}) not detected on the device.");
        return _communityVersion;
    }

    /// <summary>
    /// T-10 / T-5 readiness pass: NTP, device, authorization, screen, Xiaomi Community,
    /// and recognition of the expected unlock screen.
    /// </summary>
    public async Task<PreparationReport> PrepareAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<PreparationStep>();

        var sync = await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        steps.Add(new("NTP synchronization", sync.IsTrustworthy(Settings.MaxUncertainty),
            sync.Ok
                ? $"{sync.Server} · offset {sync.Offset.TotalMilliseconds:0} ms · uncertainty {sync.Uncertainty.TotalMilliseconds:0} ms"
                : sync.FailureReason ?? "NTP unavailable"));

        var device = await ProbeDeviceAsync(cancellationToken).ConfigureAwait(false);
        steps.Add(new("Device readiness", device.IsReady,
            device.IsReady
                ? $"{device.DisplayModel} · HyperOS {device.HyperOsVersion ?? "?"} · battery {device.BatteryLevel?.ToString() ?? "?"}%"
                : $"{device.StateLabel}: {device.Error ?? "connect the POCO F7 over USB with USB debugging authorized"}"));

        var community = device.IsReady
            ? await CheckCommunityAsync(cancellationToken).ConfigureAwait(false)
            : null;
        steps.Add(new("Xiaomi Community", community is not null,
            community is not null
                ? $"Version {community}"
                : $"Package {Settings.CommunityPackageId} not found. Sign in manually in the official app."));

        if (device.IsReady)
        {
            var capture = await _screen.CaptureAsync(device.SerialNumber, Settings.EnableEvidenceScreenshots, cancellationToken)
                .ConfigureAwait(false);
            var match = capture.Ok
                ? ScreenRecognizer.Evaluate(capture.Nodes, _profile)
                : ScreenMatch.Unknown(capture.Error ?? "Screen capture failed");

            steps.Add(new("Expected screen", match.Kind is ScreenKind.UnlockBootloader or ScreenKind.SubmissionReady,
                match.Kind == ScreenKind.Unknown
                    ? "Open Xiaomi Community → Me → Unlock Bootloader, then check readiness again."
                    : $"{match.Kind} ({match.Confidence})"));
        }
        else
        {
            steps.Add(new("Expected screen", false, "Skipped: the device is not ready."));
        }

        var ready = steps.All(s => s.Ok);
        if (ready)
        {
            WalkToReady("all readiness checks passed");
        }
        else if (!SubmissionStateMachine.IsTerminal(Machine.Current))
        {
            WalkToReady("readiness checks failed");
            SetState(SubmissionState.Failed, "readiness checks failed");
        }

        StatusChanged?.Invoke(ready ? "Readiness: all checks passed." : "Readiness: not ready.");
        return new PreparationReport(ready, steps);
    }

    /// <summary>Walks the deterministic preparation chain up to READY.</summary>
    private void WalkToReady(string reason)
    {
        if (SubmissionStateMachine.IsTerminal(Machine.Current) || Machine.Current == SubmissionState.Idle)
        {
            Machine.Reset();
            SetState(SubmissionState.Initializing, reason);
        }

        foreach (var state in new[]
                 {
                     SubmissionState.TimeSync, SubmissionState.DeviceCheck, SubmissionState.CommunityCheck,
                     SubmissionState.Preparing, SubmissionState.Ready,
                 })
        {
            if (Machine.Current != state)
            {
                SetState(state, reason);
            }
        }
    }

    /// <summary>Read-only screen recognition used by the Diagnostics section.</summary>
    public async Task<ScreenInspection> InspectScreenAsync(
        string? serial,
        CancellationToken cancellationToken = default)
    {
        var capture = await _screen.CaptureAsync(serial, Settings.EnableEvidenceScreenshots, cancellationToken)
            .ConfigureAwait(false);

        if (!capture.Ok)
        {
            return new ScreenInspection(
                ScreenMatch.Unknown(capture.Error ?? "Screen capture failed."),
                0,
                null,
                null);
        }

        var match = ScreenRecognizer.Evaluate(capture.Nodes, _profile);
        var button = ScreenRecognizer.FindSubmitButton(capture.Nodes, _profile);
        return new ScreenInspection(match, capture.Nodes.Count, button?.Text, capture.ScreenshotPath);
    }

    /// <summary>Arms the scheduler for the next 00:00:00 Asia/Shanghai and persists the attempt row.</summary>
    public async Task<AttemptRecord> ArmAsync(CancellationToken cancellationToken = default)
    {
        var target = Target;
        if (await _store.HasExecutedAtAsync(target, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("This scheduled target already executed; duplicate submission is blocked.");
        }

        _attemptId = Guid.NewGuid();
        _emergencyStopped = false;

        if (Machine.Current != SubmissionState.Ready)
        {
            WalkToReady("armed by operator");
        }

        SetState(SubmissionState.Armed, $"armed for {TargetTimeCalculator.FormatBeijing(target)}");

        var record = BuildRecord(target, SubmissionState.Armed, null);
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        if (!SubmissionStateMachine.IsTerminal(Machine.Current))
        {
            SetState(SubmissionState.Aborted, "cancelled by operator");
        }

        var record = BuildRecord(Target, Machine.Current, null) with
        {
            ErrorMessage = "Cancelled by operator.",
        };
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits with adaptive cadence (slow while far away, millisecond polling near the target),
    /// then performs the single guarded action and classifies the response.
    /// </summary>
    public async Task<ExecutionOutcome> WaitAndExecuteAsync(CancellationToken cancellationToken = default)
    {
        var target = Target;
        if (Machine.Current == SubmissionState.Ready)
        {
            SetState(SubmissionState.Armed, "auto-armed");
        }

        if (Machine.Current != SubmissionState.Armed)
        {
            throw new InvalidOperationException($"Cannot wait from state {Machine.Current}.");
        }

        SetState(SubmissionState.WaitingForTarget, $"target {TargetTimeCalculator.FormatBeijing(target)}");

        while (Now < target - TimeSpan.FromSeconds(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_emergencyStopped)
            {
                return await AbortAsync(SubmissionState.Aborted, "Emergency stop before target.", cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        // T-10s: revalidate time and connection, then final pre-execution gate.
        await SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        var device = await ProbeDeviceAsync(cancellationToken).ConfigureAwait(false);
        if (!device.IsReady)
        {
            return await AbortAsync(SubmissionState.Failed, "Device disconnected before target.", cancellationToken)
                .ConfigureAwait(false);
        }

        SetState(SubmissionState.PreExecutionValidation, "T-10s");

        var capture = await _screen.CaptureAsync(device.SerialNumber, Settings.EnableEvidenceScreenshots, cancellationToken)
            .ConfigureAwait(false);
        var match = capture.Ok
            ? ScreenRecognizer.Evaluate(capture.Nodes, _profile)
            : ScreenMatch.Unknown(capture.Error ?? "capture failed");

        var alreadyExecuted = await _store.HasExecutedAtAsync(target, cancellationToken).ConfigureAwait(false)
                              || Machine.SubmissionCount > 0;

        GateResult? gate = null;

        var button = ScreenRecognizer.FindSubmitButton(capture.Nodes, _profile);

        // Fine-grained wait: the tap is issued at the verified target instant.
        while (Now < target - TimeSpan.FromMilliseconds(220))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_emergencyStopped)
            {
                return await AbortAsync(SubmissionState.Aborted, "Emergency stop just before submission.", cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }

        // Final gate at T-0: every safety condition is verified against the live state.
        gate = PreExecutionValidator.Evaluate(new GateInput
        {
            TimeSynchronized = _lastSync.Ok,
            TimeUncertainty = _lastSync.Uncertainty,
            MaxAllowedUncertainty = Settings.MaxUncertainty,
            TargetReached = Now >= target - TimeSpan.FromMilliseconds(250),
            WindowMissed = TargetTimeCalculator.IsMissed(target, Now, Settings.LateGrace),
            DeviceReady = device.IsReady,
            ScreenVerified = match.Kind is ScreenKind.SubmissionReady or ScreenKind.UnlockBootloader,
            ButtonEnabled = button?.Enabled == true,
            SecurityChallengePresent = match.Kind == ScreenKind.SecurityChallenge,
            AlreadyExecuted = alreadyExecuted,
            EmergencyStopped = _emergencyStopped,
        });

        if (!gate.Passed)
        {
            StatusChanged?.Invoke(gate.Summary);
            return await AbortAsync(SubmissionState.Aborted, gate.Summary, cancellationToken, gate)
                .ConfigureAwait(false);
        }

        if (button is null || button.Bounds.IsEmpty)
        {
            return await AbortAsync(SubmissionState.Failed, "Submission button has no usable bounds.",
                cancellationToken, gate).ConfigureAwait(false);
        }

        SetState(SubmissionState.Executing, "single submission action");

        var record = BuildRecord(target, SubmissionState.Executing, match) with
        {
            TriggeredAtUtc = Now,
            UiVerification = $"{match.Kind} ({match.Confidence}) via {button.ClassName} '{button.Text}'",
        };
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        var tapped = await _adb.TapAsync(button.Bounds.CenterX, button.Bounds.CenterY, cancellationToken)
            .ConfigureAwait(false);

        SetState(SubmissionState.WaitingForResponse, tapped ? "tap sent" : "tap command failed");

        var executed = record with
        {
            AdbCompletedAtUtc = Now,
            State = SubmissionState.WaitingForResponse,
            ErrorMessage = tapped ? null : "adb input tap returned a failure.",
        };
        await _store.SaveAsync(executed, cancellationToken).ConfigureAwait(false);

        // Observe the real response instead of assuming the tap succeeded.
        await Task.Delay(TimeSpan.FromMilliseconds(1800), cancellationToken).ConfigureAwait(false);
        var responseCapture = await _screen.CaptureAsync(device.SerialNumber, Settings.EnableEvidenceScreenshots, cancellationToken)
            .ConfigureAwait(false);
        var classification = responseCapture.Ok
            ? ResultClassifier.Classify(responseCapture.Nodes, _profile)
            : new Classification(FinalResult.Unknown, ConfidenceLevel.Low, "Response screen could not be captured.", ScreenKind.Unknown);

        var nextState = classification.Result switch
        {
            FinalResult.Approved => SubmissionState.Approved,
            FinalResult.Rejected => SubmissionState.Rejected,
            FinalResult.QuotaExhausted => SubmissionState.QuotaExhausted,
            FinalResult.AccountIneligible => SubmissionState.AccountIneligible,
            FinalResult.NetworkError => SubmissionState.NetworkError,
            FinalResult.SecurityChallenge => SubmissionState.SecurityChallenge,
            FinalResult.Submitted => SubmissionState.Submitted,
            _ => SubmissionState.UnknownResult,
        };

        // SUBMITTED is the acknowledgement step; APPROVED/REJECTED follow from it.
        if (nextState is SubmissionState.Approved or SubmissionState.Rejected
            && Machine.Current == SubmissionState.WaitingForResponse)
        {
            SetState(SubmissionState.Submitted, "Xiaomi response observed");
        }

        if (SubmissionStateMachine.CanTransition(Machine.Current, nextState))
        {
            SetState(nextState, classification.Evidence);
        }

        var finalRecord = executed with
        {
            State = nextState,
            Result = classification.Result,
            Confidence = classification.Confidence,
            ResponseObservedAtUtc = Now,
            ScreenshotPath = responseCapture.ScreenshotPath ?? capture.ScreenshotPath,
            ErrorMessage = classification.IsAmbiguous
                ? "Ambiguous result: manual verification required, no automatic retry."
                : null,
        };
        await _store.SaveAsync(finalRecord, cancellationToken).ConfigureAwait(false);

        StatusChanged?.Invoke($"{classification.Result} ({classification.Confidence}) — {classification.Evidence}");
        return new ExecutionOutcome(classification, finalRecord, gate, tapped);
    }

    private async Task<ExecutionOutcome> AbortAsync(
        SubmissionState state,
        string reason,
        CancellationToken cancellationToken,
        GateResult? gate = null)
    {
        SetState(state, reason);
        var record = BuildRecord(Target, state, null) with
        {
            ErrorMessage = reason,
            Result = FinalResult.Unknown,
            Confidence = ConfidenceLevel.Low,
            ResponseObservedAtUtc = Now,
        };
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return new ExecutionOutcome(
            new Classification(FinalResult.Unknown, ConfidenceLevel.Low, reason, ScreenKind.Unknown),
            record,
            gate,
            false);
    }

    private AttemptRecord BuildRecord(DateTimeOffset target, SubmissionState state, ScreenMatch? match) => new()
    {
        AttemptId = _attemptId,
        ScheduledBeijing = TargetTimeCalculator.ToBeijing(target),
        ScheduledJakarta = TargetTimeCalculator.ToJakarta(target),
        NtpServer = _lastSync.Server,
        NtpOffsetMs = _lastSync.Ok ? _lastSync.Offset.TotalMilliseconds : null,
        TimeUncertaintyMs = _lastSync.Ok ? _lastSync.Uncertainty.TotalMilliseconds : null,
        NetworkLatencyMs = _lastSync.Ok ? _lastSync.BestRoundTrip.TotalMilliseconds : null,
        DeviceModel = _lastDevice.IsReady ? _lastDevice.DisplayModel : null,
        HyperOsVersion = _lastDevice.HyperOsVersion,
        AdbStatus = _lastDevice.StateLabel,
        CommunityVersion = _communityVersion,
        Mode = Settings.ExecutionMode,
        UiVerification = match is null ? null : $"{match.Kind} ({match.Confidence})",
        State = state,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };
}
