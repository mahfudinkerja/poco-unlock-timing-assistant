using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Configuration;
using PocoUnlockAssistant.Core.Persistence;
using PocoUnlockAssistant.Core.Scheduling;
using PocoUnlockAssistant.Core.Time;
using PocoUnlockAssistant.Infrastructure.Persistence;
using Xunit;

namespace PocoUnlockAssistant.Tests;

public sealed class SchedulerTests : IDisposable
{
    private static readonly ScreenRecognitionProfile Profile = ScreenRecognitionProfile.CreateDefault();

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"poco-assistant-test-{Guid.NewGuid():N}");

    private readonly string _databasePath;

    public SchedulerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _databasePath = Path.Combine(_tempDirectory, "attempts.db");
    }

    private static UiNode Node(string text, bool clickable = false, bool enabled = true) =>
        new(text, string.Empty, string.Empty, "android.widget.TextView", enabled, clickable, new UiBounds(0, 0, 100, 50));

    private static IReadOnlyList<UiNode> UnlockScreen =>
    [
        Node("Unlock Bootloader"),
        new UiNode("Apply", "com.mi.global.bbs:id/primary_button", string.Empty, "android.widget.Button",
            true, true, new UiBounds(100, 1800, 880, 140)),
    ];

    private static IReadOnlyList<UiNode> ApprovedScreen => [Node("Unlock request approved")];

    private static IReadOnlyList<UiNode> UnrelatedScreen => [Node("Unrelated settings page")];

    private async Task<SqliteAttemptStore> NewStoreAsync()
    {
        var store = new SqliteAttemptStore(_databasePath);
        await store.InitializeAsync();
        return store;
    }

    private static SubmissionCoordinator NewCoordinator(
        FakeNtpClient ntp,
        FakeAdbClient adb,
        FakeScreenInspector screen,
        SqliteAttemptStore store,
        out MonotonicClock clock,
        DateTimeOffset? now = null)
    {
        clock = new MonotonicClock(now ?? DateTimeOffset.UtcNow);
        return new SubmissionCoordinator(
            ntp, adb, screen, store,
            new AssistantSettings(),
            Profile,
            clock);
    }

    [Fact]
    public async Task PreparationSucceedsOnHealthyDevice()
    {
        var store = await NewStoreAsync();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);

        var coordinator = NewCoordinator(new FakeNtpClient(), new FakeAdbClient(), screen, store, out _);

        var report = await coordinator.PrepareAsync();

        Assert.True(report.Ready);
        Assert.Equal(4, report.Steps.Count);
        Assert.All(report.Steps, s => Assert.True(s.Ok));
        Assert.Equal(SubmissionState.Ready, coordinator.Machine.Current);
    }

    [Fact]
    public async Task PreparationFailsWhenNtpFails()
    {
        var store = await NewStoreAsync();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);

        var coordinator = NewCoordinator(
            new FakeNtpClient { Fail = true }, new FakeAdbClient(), screen, store, out _);

        var report = await coordinator.PrepareAsync();

        Assert.False(report.Ready);
        Assert.Contains(report.Steps, s => s.Name.StartsWith("NTP") && !s.Ok);
    }

    [Fact]
    public async Task PreparationFailsWithoutDevice()
    {
        var store = await NewStoreAsync();
        var screen = new FakeScreenInspector();

        var coordinator = NewCoordinator(
            new FakeNtpClient(), new FakeAdbClient { DeviceReady = false }, screen, store, out _);

        var report = await coordinator.PrepareAsync();

        Assert.False(report.Ready);
        Assert.Contains(report.Steps, s => s.Name == "Device readiness" && !s.Ok);
    }

    [Fact]
    public async Task ScheduledRunSendsExactlyOneActionAndRecordsTheResponse()
    {
        var store = await NewStoreAsync();
        var adb = new FakeAdbClient();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);
        screen.Enqueue(ApprovedScreen);

        var clock = new MonotonicClock(DateTimeOffset.UtcNow);
        var coordinator = new SubmissionCoordinator(
            new FakeNtpClient(), adb, screen, store, new AssistantSettings(), Profile, clock)
        {
            TargetOverride = DateTimeOffset.UtcNow.AddMilliseconds(600),
        };

        await coordinator.ArmAsync();
        Assert.Equal(SubmissionState.Armed, coordinator.Machine.Current);

        var outcome = await coordinator.WaitAndExecuteAsync();

        Assert.True(outcome.ActionSent);
        Assert.Equal(1, adb.TapCount);
        Assert.Equal(FinalResult.Approved, outcome.Classification.Result);
        Assert.Equal(SubmissionState.Approved, coordinator.Machine.Current);
        Assert.NotNull(outcome.Record.TriggeredAtUtc);
        Assert.NotNull(outcome.Record.AdbCompletedAtUtc);
        Assert.NotNull(outcome.Record.ResponseObservedAtUtc);
        Assert.Equal(1, coordinator.Machine.SubmissionCount);

        var stored = await store.GetRecentAsync();
        Assert.Single(stored);
        Assert.Equal(FinalResult.Approved, stored[0].Result);
        Assert.NotNull(stored[0].TriggeredAtUtc);
    }

    [Fact]
    public async Task SecondArmForTheSameTargetIsRejected()
    {
        var store = await NewStoreAsync();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);
        screen.Enqueue(ApprovedScreen);

        var clock = new MonotonicClock(DateTimeOffset.UtcNow);
        var target = DateTimeOffset.UtcNow.AddMilliseconds(500);
        var coordinator = new SubmissionCoordinator(
            new FakeNtpClient(), new FakeAdbClient(), screen, store, new AssistantSettings(), Profile, clock)
        {
            TargetOverride = target,
        };

        await coordinator.ArmAsync();
        await coordinator.WaitAndExecuteAsync();

        var second = new SubmissionCoordinator(
            new FakeNtpClient(), new FakeAdbClient(), screen, store, new AssistantSettings(), Profile,
            new MonotonicClock(DateTimeOffset.UtcNow))
        {
            TargetOverride = target,
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => second.ArmAsync());
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissedWindowDoesNotSubmitLate()
    {
        var store = await NewStoreAsync();
        var adb = new FakeAdbClient();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);

        var clock = new MonotonicClock(DateTimeOffset.UtcNow);
        var coordinator = new SubmissionCoordinator(
            new FakeNtpClient(), adb, screen, store, new AssistantSettings(), Profile, clock)
        {
            TargetOverride = DateTimeOffset.UtcNow.AddMinutes(-2),
        };

        await coordinator.ArmAsync();
        var outcome = await coordinator.WaitAndExecuteAsync();

        Assert.False(outcome.ActionSent);
        Assert.Equal(0, adb.TapCount);
        Assert.Equal(SubmissionState.Aborted, coordinator.Machine.Current);
        Assert.Contains("late submission is not allowed", outcome.Classification.Evidence,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmergencyStopPreventsTheSubmission()
    {
        var store = await NewStoreAsync();
        var adb = new FakeAdbClient();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);

        var clock = new MonotonicClock(DateTimeOffset.UtcNow);
        var coordinator = new SubmissionCoordinator(
            new FakeNtpClient(), adb, screen, store, new AssistantSettings(), Profile, clock)
        {
            TargetOverride = DateTimeOffset.UtcNow.AddSeconds(3),
        };

        await coordinator.ArmAsync();
        coordinator.EmergencyStop();

        var outcome = await coordinator.WaitAndExecuteAsync();

        Assert.False(outcome.ActionSent);
        Assert.Equal(0, adb.TapCount);
        Assert.Equal(SubmissionState.Aborted, coordinator.Machine.Current);
    }

    [Fact]
    public async Task WrongScreenBlocksTheSubmission()
    {
        var store = await NewStoreAsync();
        var adb = new FakeAdbClient();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnrelatedScreen);

        var clock = new MonotonicClock(DateTimeOffset.UtcNow);
        var coordinator = new SubmissionCoordinator(
            new FakeNtpClient(), adb, screen, store, new AssistantSettings(), Profile, clock)
        {
            TargetOverride = DateTimeOffset.UtcNow.AddMilliseconds(500),
        };

        await coordinator.ArmAsync();
        var outcome = await coordinator.WaitAndExecuteAsync();

        Assert.False(outcome.ActionSent);
        Assert.Equal(0, adb.TapCount);
        Assert.Equal(SubmissionState.Aborted, coordinator.Machine.Current);
        Assert.NotNull(outcome.Gate);
        Assert.Contains(outcome.Gate!.Failures, f => f.Name == "screen-verified");
    }

    [Fact]
    public async Task CancellationAbortsTheAttempt()
    {
        var store = await NewStoreAsync();
        var screen = new FakeScreenInspector();
        screen.Enqueue(UnlockScreen);

        var coordinator = NewCoordinator(new FakeNtpClient(), new FakeAdbClient(), screen, store, out _);

        await coordinator.PrepareAsync();
        await coordinator.ArmAsync();
        await coordinator.CancelAsync();

        Assert.Equal(SubmissionState.Aborted, coordinator.Machine.Current);

        var stored = await store.GetRecentAsync();
        Assert.Contains(stored, r => r.State == SubmissionState.Aborted);
    }

    [Fact]
    public async Task AuditStoreSurvivesRestartAndExports()
    {
        var store = await NewStoreAsync();
        var record = new AttemptRecord
        {
            AttemptId = Guid.NewGuid(),
            ScheduledBeijing = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.FromHours(8)),
            ScheduledJakarta = new DateTimeOffset(2026, 11, 25, 23, 0, 0, TimeSpan.FromHours(7)),
            NtpServer = "time.cloudflare.com",
            NtpOffsetMs = 12.4,
            TimeUncertaintyMs = 9.2,
            DeviceModel = "POCO F7",
            Mode = ExecutionMode.ControlledAdb,
            Result = FinalResult.QuotaExhausted,
            Confidence = ConfidenceLevel.High,
            State = SubmissionState.QuotaExhausted,
            TriggeredAtUtc = DateTimeOffset.UtcNow,
            ScreenshotPath = @"C:\evidence\screen.png",
        };

        await store.SaveAsync(record);

        var reopened = new SqliteAttemptStore(_databasePath);
        await reopened.InitializeAsync();
        var loaded = await reopened.GetRecentAsync();

        var saved = Assert.Single(loaded);
        Assert.Equal(record.AttemptId, saved.AttemptId);
        Assert.Equal(FinalResult.QuotaExhausted, saved.Result);
        Assert.Equal("POCO F7", saved.DeviceModel);
        Assert.Equal(12.4, saved.NtpOffsetMs!.Value, 3);

        Assert.True(await reopened.HasExecutedAtAsync(record.ScheduledBeijing));

        var csvPath = await reopened.ExportCsvAsync();
        var jsonPath = await reopened.ExportJsonAsync();
        Assert.True(File.Exists(csvPath));
        Assert.Contains("QuotaExhausted", await File.ReadAllTextAsync(csvPath));
        Assert.Contains("QuotaExhausted", await File.ReadAllTextAsync(jsonPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary test data; ignore cleanup failures.
        }
    }
}
