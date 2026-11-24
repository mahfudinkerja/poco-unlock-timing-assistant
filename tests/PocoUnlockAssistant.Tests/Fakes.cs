using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Device;
using PocoUnlockAssistant.Core.Time;

namespace PocoUnlockAssistant.Tests;

internal sealed class FakeNtpClient : INtpClient
{
    public bool Fail { get; set; }

    public TimeSyncReport? ForcedReport { get; set; }

    public Task<TimeSyncReport> SynchronizeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Fail
            ? TimeSyncReport.Failure("simulated NTP failure", DateTimeOffset.UtcNow)
            : ForcedReport ?? new TimeSyncReport
            {
                Ok = true,
                Health = SyncHealth.Healthy,
                MeasuredAtUtc = DateTimeOffset.UtcNow,
                Offset = TimeSpan.FromMilliseconds(5),
                Uncertainty = TimeSpan.FromMilliseconds(8),
                BestRoundTrip = TimeSpan.FromMilliseconds(16),
                Server = "fake.ntp",
                Stratum = 2,
                Samples = [new NtpSample("fake.ntp", TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(16), 2)],
            });
}

internal sealed class FakeAdbClient : IAdbClient
{
    public int TapCount { get; private set; }

    public bool TapSucceeds { get; set; } = true;

    public bool DeviceReady { get; set; } = true;

    public bool CommunityInstalled { get; set; } = true;

    public AdbDeviceInfo ProbeResult { get; set; } = new()
    {
        AdbAvailable = true,
        State = AdbConnectionState.Ready,
        Model = "2406APNCAA",
        MarketName = "POCO F7",
        HyperOsVersion = "OS3.0.2.0",
        AndroidVersion = "15",
        SerialNumber = "ABC123",
        UsbState = "mtp,adb",
        BatteryLevel = 90,
        ScreenOn = true,
    };

    public Task<AdbDeviceInfo> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DeviceReady
            ? ProbeResult
            : ProbeResult with { State = AdbConnectionState.NotAttached });

    public Task<AdbCommandResult> ExecuteAsync(string arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdbCommandResult(true, string.Empty, string.Empty, 0));

    public Task<byte[]?> ExecuteBinaryAsync(string arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>([]);

    public Task<bool> LaunchAppAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommunityInstalled);

    public Task<bool> TapAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        TapCount++;
        return Task.FromResult(TapSucceeds);
    }

    public Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CommunityInstalled);

    public Task<string?> GetPackageVersionAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(CommunityInstalled ? "7.2.1" : null);
}

/// <summary>Returns queued hierarchies in order, then repeats the last one.</summary>
internal sealed class FakeScreenInspector : IScreenInspector
{
    private readonly Queue<IReadOnlyList<UiNode>> _queue = new();
    private IReadOnlyList<UiNode> _last = [];

    public int CaptureCount { get; private set; }

    public void Enqueue(IReadOnlyList<UiNode> nodes) => _queue.Enqueue(nodes);

    public Task<ScreenCapture> CaptureAsync(
        string? serial,
        bool saveScreenshot,
        CancellationToken cancellationToken = default)
    {
        CaptureCount++;
        _last = _queue.Count > 0 ? _queue.Dequeue() : _last;
        return Task.FromResult(new ScreenCapture(
            true,
            _last,
            saveScreenshot ? "C:\\evidence\\screen.png" : null,
            DateTimeOffset.UtcNow,
            null));
    }
}
