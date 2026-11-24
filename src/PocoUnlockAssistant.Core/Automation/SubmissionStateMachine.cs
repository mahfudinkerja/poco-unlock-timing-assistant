namespace PocoUnlockAssistant.Core.Automation;

public enum SubmissionState
{
    Idle,
    Initializing,
    TimeSync,
    DeviceCheck,
    CommunityCheck,
    Preparing,
    Ready,
    Armed,
    WaitingForTarget,
    PreExecutionValidation,
    Executing,
    WaitingForResponse,
    Submitted,
    Approved,
    Rejected,
    QuotaExhausted,
    AccountIneligible,
    NetworkError,
    SecurityChallenge,
    UnknownResult,
    Aborted,
    Failed,
}

public sealed record StateTransition(SubmissionState From, SubmissionState To, DateTimeOffset AtUtc, string? Reason = null);

/// <summary>
/// Deterministic guard around the submission flow. Illegal transitions are rejected
/// instead of silently accepted, and no attempt can enter EXECUTING twice.
/// </summary>
public sealed class SubmissionStateMachine
{
    private readonly List<StateTransition> _history = [];
    private readonly object _gate = new();

    public SubmissionStateMachine(SubmissionState initial = SubmissionState.Idle)
    {
        Current = initial;
    }

    public SubmissionState Current { get; private set; }

    public int SubmissionCount { get; private set; }

    public IReadOnlyList<StateTransition> History
    {
        get { lock (_gate) { return _history.ToArray(); } }
    }

    public event EventHandler<StateTransition>? Transitioned;

    private static readonly Dictionary<SubmissionState, SubmissionState[]> Allowed = new()
    {
        [SubmissionState.Idle] = [SubmissionState.Initializing, SubmissionState.Aborted],
        [SubmissionState.Initializing] = [SubmissionState.TimeSync, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.TimeSync] = [SubmissionState.DeviceCheck, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.DeviceCheck] = [SubmissionState.CommunityCheck, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.CommunityCheck] = [SubmissionState.Preparing, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.Preparing] = [SubmissionState.Ready, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.Ready] = [SubmissionState.Armed, SubmissionState.Failed, SubmissionState.Aborted],
        [SubmissionState.Armed] = [SubmissionState.WaitingForTarget, SubmissionState.Aborted, SubmissionState.Failed],
        [SubmissionState.WaitingForTarget] =
            [SubmissionState.PreExecutionValidation, SubmissionState.Aborted, SubmissionState.Failed],
        [SubmissionState.PreExecutionValidation] =
            [SubmissionState.Executing, SubmissionState.Aborted, SubmissionState.Failed, SubmissionState.SecurityChallenge],
        [SubmissionState.Executing] = [SubmissionState.WaitingForResponse, SubmissionState.Failed],
        [SubmissionState.WaitingForResponse] =
        [
            SubmissionState.Submitted, SubmissionState.NetworkError, SubmissionState.SecurityChallenge,
            SubmissionState.UnknownResult, SubmissionState.QuotaExhausted, SubmissionState.AccountIneligible,
            SubmissionState.Aborted,
        ],
        [SubmissionState.Submitted] =
        [
            SubmissionState.Approved, SubmissionState.Rejected, SubmissionState.QuotaExhausted,
            SubmissionState.AccountIneligible, SubmissionState.UnknownResult,
        ],
    };

    public static bool IsTerminal(SubmissionState state) => state switch
    {
        SubmissionState.Approved or SubmissionState.Rejected or SubmissionState.QuotaExhausted
            or SubmissionState.AccountIneligible or SubmissionState.NetworkError
            or SubmissionState.SecurityChallenge or SubmissionState.UnknownResult
            or SubmissionState.Aborted or SubmissionState.Failed => true,
        _ => false,
    };

    public static bool CanTransition(SubmissionState from, SubmissionState to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public bool TryTransition(SubmissionState next, DateTimeOffset atUtc, out string? error, string? reason = null)
    {
        lock (_gate)
        {
            if (next == SubmissionState.Executing && SubmissionCount >= 1)
            {
                error = "Duplicate submission blocked: this attempt has already triggered one action.";
                return false;
            }

            if (!CanTransition(Current, next))
            {
                error = $"Invalid transition {Current} -> {next}.";
                return false;
            }

            var transition = new StateTransition(Current, next, atUtc, reason);
            Current = next;
            if (next == SubmissionState.Executing)
            {
                SubmissionCount++;
            }

            _history.Add(transition);
            error = null;
        }

        Transitioned?.Invoke(this, new StateTransition(Current, Current, atUtc));
        return true;
    }

    public void Transition(SubmissionState next, DateTimeOffset atUtc, string? reason = null)
    {
        if (!TryTransition(next, atUtc, out var error, reason))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Returns the machine to IDLE for a brand-new attempt ID.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Current = SubmissionState.Idle;
            SubmissionCount = 0;
            _history.Clear();
        }
    }
}
