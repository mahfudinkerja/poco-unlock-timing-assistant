using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Device;
using PocoUnlockAssistant.Core.Persistence;
using PocoUnlockAssistant.Core.Time;

namespace PocoUnlockAssistant.Core.Abstractions;

public interface INtpClient
{
    Task<TimeSyncReport> SynchronizeAsync(CancellationToken cancellationToken = default);
}

public interface IAdbClient
{
    Task<AdbDeviceInfo> ProbeAsync(CancellationToken cancellationToken = default);

    Task<AdbCommandResult> ExecuteAsync(string arguments, CancellationToken cancellationToken = default);

    Task<byte[]?> ExecuteBinaryAsync(string arguments, CancellationToken cancellationToken = default);

    Task<bool> LaunchAppAsync(string packageId, CancellationToken cancellationToken = default);

    Task<bool> TapAsync(int x, int y, CancellationToken cancellationToken = default);

    Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default);

    Task<string?> GetPackageVersionAsync(string packageId, CancellationToken cancellationToken = default);
}

public sealed record AdbCommandResult(bool Ok, string StandardOutput, string StandardError, int ExitCode)
{
    public static AdbCommandResult Failure(string error) => new(false, string.Empty, error, -1);
}

/// <summary>A captured screen: normalized UI hierarchy plus an optional PNG evidence file.</summary>
public sealed record ScreenCapture(
    bool Ok,
    IReadOnlyList<UiNode> Nodes,
    string? ScreenshotPath,
    DateTimeOffset CapturedAtUtc,
    string? Error)
{
    public static ScreenCapture Failure(string error, DateTimeOffset atUtc) => new(false, [], null, atUtc, error);
}

public interface IScreenInspector
{
    Task<ScreenCapture> CaptureAsync(string? serial, bool saveScreenshot, CancellationToken cancellationToken = default);
}

public interface IAttemptStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AttemptRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttemptRecord>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default);

    Task<AttemptRecord?> FindByScheduledAsync(DateTimeOffset scheduledBeijing, CancellationToken cancellationToken = default);

    Task<bool> HasExecutedAtAsync(DateTimeOffset scheduledBeijing, CancellationToken cancellationToken = default);

    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);
}
