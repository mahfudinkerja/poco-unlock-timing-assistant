using PocoUnlockAssistant.Core.Automation;
using Xunit;

namespace PocoUnlockAssistant.Tests;

public class AutomationTests
{
    private static readonly ScreenRecognitionProfile Profile = ScreenRecognitionProfile.CreateDefault();

    private static UiNode Node(string text, bool clickable = false, bool enabled = true, string id = "") =>
        new(text, id, string.Empty, "android.widget.TextView", enabled, clickable, new UiBounds(10, 10, 200, 60));

    private static UiNode Button(string text, bool enabled = true) =>
        new(text, "com.miui.community:id/primary_button", string.Empty, "android.widget.Button",
            enabled, true, new UiBounds(100, 1800, 880, 140));

    [Fact]
    public void RecognizesCorrectUnlockScreenWithEnabledButton()
    {
        UiNode[] nodes = [Node("Unlock Bootloader"), Node("Requirements"), Button("Apply")];

        var match = ScreenRecognizer.Evaluate(nodes, Profile);

        Assert.Equal(ScreenKind.SubmissionReady, match.Kind);
        Assert.Equal(ConfidenceLevel.High, match.Confidence);
        Assert.Contains("button:Apply", match.Evidence);
    }

    [Fact]
    public void DisabledButton_LowersConfidence()
    {
        UiNode[] nodes = [Node("Unlock Bootloader"), Button("Apply", enabled: false)];

        var match = ScreenRecognizer.Evaluate(nodes, Profile);

        Assert.Equal(ScreenKind.SubmissionReady, match.Kind);
        Assert.Equal(ConfidenceLevel.Low, match.Confidence);
    }

    [Fact]
    public void MissingButton_IsNotSubmissionReady()
    {
        UiNode[] nodes = [Node("Unlock Bootloader"), Node("Requirements")];

        var match = ScreenRecognizer.Evaluate(nodes, Profile);

        Assert.Equal(ScreenKind.UnlockBootloader, match.Kind);
    }

    [Fact]
    public void WrongOrChangedScreen_IsUnknown()
    {
        UiNode[] nodes = [Node("Some unrelated settings page"), Node("Wi-Fi")];

        var match = ScreenRecognizer.Evaluate(nodes, Profile);

        Assert.Equal(ScreenKind.Unknown, match.Kind);
        Assert.NotEmpty(match.Evidence);
        Assert.Equal(ScreenKind.Unknown, ResultClassifier.Classify(nodes, Profile).Screen);
    }

    [Fact]
    public void EmptyHierarchy_IsUnknown()
    {
        var match = ScreenRecognizer.Evaluate([], Profile);

        Assert.Equal(ScreenKind.Unknown, match.Kind);
        Assert.Equal(ConfidenceLevel.Low, match.Confidence);
    }

    [Fact]
    public void CaptchaBlocksAutomation()
    {
        UiNode[] nodes = [Node("Security check"), Node("Slide to verify you are human"), Node("Unlock Bootloader")];

        var match = ScreenRecognizer.Evaluate(nodes, Profile);
        var classification = ResultClassifier.Classify(nodes, Profile);

        Assert.Equal(ScreenKind.SecurityChallenge, match.Kind);
        Assert.Equal(FinalResult.SecurityChallenge, classification.Result);
        Assert.False(classification.IsAmbiguous);
    }

    [Fact]
    public void ExplicitApproval_IsClassifiedAsApproved()
    {
        UiNode[] nodes = [Node("Unlock request approved"), Node("You can unlock your device now")];

        var classification = ResultClassifier.Classify(nodes, Profile);

        Assert.Equal(FinalResult.Approved, classification.Result);
        Assert.Equal(ConfidenceLevel.High, classification.Confidence);
    }

    [Fact]
    public void AmbiguousAcknowledgement_IsSubmittedNotApproved()
    {
        UiNode[] nodes = [Node("Request submitted successfully")];

        var classification = ResultClassifier.Classify(nodes, Profile);

        Assert.Equal(FinalResult.Submitted, classification.Result);
        Assert.NotEqual(FinalResult.Approved, classification.Result);
    }

    [Fact]
    public void UnrecognizedResponse_StaysUnknownAndIsAmbiguous()
    {
        UiNode[] nodes = [Node("Something unexpected happened")];

        var classification = ResultClassifier.Classify(nodes, Profile);

        Assert.Equal(FinalResult.Unknown, classification.Result);
        Assert.True(classification.IsAmbiguous);
    }

    [Theory]
    [InlineData("Daily quota exhausted, try again tomorrow", FinalResult.QuotaExhausted)]
    [InlineData("Your account is not eligible for unlocking", FinalResult.AccountIneligible)]
    [InlineData("Network error, connection failed", FinalResult.NetworkError)]
    [InlineData("Request rejected", FinalResult.Rejected)]
    public void ResultCategories_AreRecognized(string text, FinalResult expected)
    {
        var classification = ResultClassifier.Classify([Node(text)], Profile);

        Assert.Equal(expected, classification.Result);
    }

    [Fact]
    public void TapAloneIsNotSuccess()
    {
        UiNode[] nodes = [Node("Unlock Bootloader"), Button("Apply")];

        var classification = ResultClassifier.Classify(nodes, Profile);

        Assert.Equal(FinalResult.Unknown, classification.Result);
        Assert.Contains("no response has been confirmed", classification.Evidence);
    }

    [Fact]
    public void GatePassesWhenEveryConditionHolds()
    {
        var gate = PreExecutionValidator.Evaluate(AllPassing());

        Assert.True(gate.Passed);
        Assert.All(gate.Checks, c => Assert.True(c.Passed));
    }

    public static GateInput AllPassing() => new()
    {
        TimeSynchronized = true,
        TimeUncertainty = TimeSpan.FromMilliseconds(20),
        MaxAllowedUncertainty = TimeSpan.FromMilliseconds(150),
        TargetReached = true,
        WindowMissed = false,
        DeviceReady = true,
        ScreenVerified = true,
        ButtonEnabled = true,
        SecurityChallengePresent = false,
        AlreadyExecuted = false,
        EmergencyStopped = false,
    };

    [Theory]
    [InlineData("time-uncertainty")]
    [InlineData("device-ready")]
    [InlineData("screen-verified")]
    [InlineData("button-enabled")]
    [InlineData("window-not-missed")]
    [InlineData("no-security-challenge")]
    [InlineData("no-duplicate")]
    [InlineData("not-emergency-stopped")]
    public void AnyFailedConditionBlocksExecution(string expectedFailure)
    {
        var input = expectedFailure switch
        {
            "time-uncertainty" => AllPassing() with { TimeUncertainty = TimeSpan.FromMilliseconds(500) },
            "device-ready" => AllPassing() with { DeviceReady = false },
            "screen-verified" => AllPassing() with { ScreenVerified = false },
            "button-enabled" => AllPassing() with { ButtonEnabled = false },
            "window-not-missed" => AllPassing() with { WindowMissed = true },
            "no-security-challenge" => AllPassing() with { SecurityChallengePresent = true },
            "no-duplicate" => AllPassing() with { AlreadyExecuted = true },
            "not-emergency-stopped" => AllPassing() with { EmergencyStopped = true },
            _ => throw new ArgumentOutOfRangeException(nameof(expectedFailure)),
        };

        var gate = PreExecutionValidator.Evaluate(input);

        Assert.False(gate.Passed);
        Assert.Contains(gate.Failures, f => f.Name == expectedFailure);
    }

    [Fact]
    public void StateMachineWalksTheHappyPath()
    {
        var machine = new SubmissionStateMachine();
        var now = DateTimeOffset.UtcNow;

        foreach (var state in new[]
                 {
                     SubmissionState.Initializing, SubmissionState.TimeSync, SubmissionState.DeviceCheck,
                     SubmissionState.CommunityCheck, SubmissionState.Preparing, SubmissionState.Ready,
                     SubmissionState.Armed, SubmissionState.WaitingForTarget,
                     SubmissionState.PreExecutionValidation, SubmissionState.Executing,
                     SubmissionState.WaitingForResponse, SubmissionState.Submitted, SubmissionState.Approved,
                 })
        {
            machine.Transition(state, now);
        }

        Assert.Equal(SubmissionState.Approved, machine.Current);
        Assert.Equal(1, machine.SubmissionCount);
        Assert.Equal(13, machine.History.Count);
        Assert.True(SubmissionStateMachine.IsTerminal(machine.Current));
    }

    [Fact]
    public void InvalidTransitionsAreRejected()
    {
        var machine = new SubmissionStateMachine();

        Assert.False(machine.TryTransition(SubmissionState.Approved, DateTimeOffset.UtcNow, out var error));
        Assert.NotNull(error);
        Assert.Equal(SubmissionState.Idle, machine.Current);
        Assert.Empty(machine.History);
    }

    [Fact]
    public void DuplicateSubmissionIsBlocked()
    {
        var machine = new SubmissionStateMachine();
        var now = DateTimeOffset.UtcNow;

        machine.Transition(SubmissionState.Initializing, now);
        machine.Transition(SubmissionState.TimeSync, now);
        machine.Transition(SubmissionState.DeviceCheck, now);
        machine.Transition(SubmissionState.CommunityCheck, now);
        machine.Transition(SubmissionState.Preparing, now);
        machine.Transition(SubmissionState.Ready, now);
        machine.Transition(SubmissionState.Armed, now);
        machine.Transition(SubmissionState.WaitingForTarget, now);
        machine.Transition(SubmissionState.PreExecutionValidation, now);
        machine.Transition(SubmissionState.Executing, now);
        machine.Transition(SubmissionState.WaitingForResponse, now);
        machine.Transition(SubmissionState.UnknownResult, now);

        Assert.False(machine.TryTransition(SubmissionState.Executing, now, out var error));
        Assert.Contains("Duplicate submission", error);
        Assert.Equal(1, machine.SubmissionCount);
    }
}
