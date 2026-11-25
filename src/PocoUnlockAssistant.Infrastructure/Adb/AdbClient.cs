using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Device;

namespace PocoUnlockAssistant.Infrastructure.Adb;

/// <summary>
/// Thin, auditable wrapper around the Android Debug Bridge. It never bypasses USB
/// debugging authorization: an unauthorized device is reported as such and nothing else.
/// </summary>
public sealed class AdbClient : IAdbClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<AdbClient> _logger;
    private string _executable;

    public AdbClient(string? adbPath = null, ILogger<AdbClient>? logger = null)
    {
        _logger = logger ?? NullLogger<AdbClient>.Instance;
        _executable = ResolveExecutable(adbPath);
    }

    public string Executable => _executable;

    /// <summary>Re-resolves adb after the operator changes Adb Path in Settings.</summary>
    public void UseAdbPath(string? configured) => _executable = ResolveExecutable(configured);

    public static string ResolveExecutable(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }

        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                          ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            var name = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            candidates.Add(Path.Combine(androidHome, "platform-tools", name));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to PATH resolution; a missing binary surfaces as AdbUnavailable.
        return OperatingSystem.IsWindows() ? "adb.exe" : "adb";
    }

    public async Task<AdbDeviceInfo> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var version = await ExecuteAsync("version", cancellationToken).ConfigureAwait(false);
        if (!version.Ok)
        {
            return AdbDeviceInfo.Unavailable(version.StandardError);
        }

        var adbVersion = version.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

        var list = await ExecuteAsync("devices -l", cancellationToken).ConfigureAwait(false);
        if (!list.Ok)
        {
            return AdbDeviceInfo.Unavailable(list.StandardError);
        }

        var devices = AdbOutputParser.ParseDeviceList(list.StandardOutput);
        if (devices.Count == 0)
        {
            return new AdbDeviceInfo
            {
                AdbAvailable = true,
                State = AdbConnectionState.NotAttached,
                Devices = devices,
                AdbVersion = adbVersion,
            };
        }

        var primary = devices.FirstOrDefault(d => d.IsAuthorized);
        if (primary is null)
        {
            return AdbOutputParser.BuildDeviceInfo(devices, new Dictionary<string, string>(), null, null, adbVersion);
        }

        var props = await ExecuteAsync($"-s {primary.Serial} shell getprop", cancellationToken).ConfigureAwait(false);
        var properties = props.Ok
            ? AdbOutputParser.ParseProperties(props.StandardOutput)
            : new Dictionary<string, string>();

        var battery = await ExecuteAsync($"-s {primary.Serial} shell dumpsys battery", cancellationToken).ConfigureAwait(false);
        var batteryLevel = ParseBatteryLevel(battery.StandardOutput);

        var power = await ExecuteAsync($"-s {primary.Serial} shell dumpsys power", cancellationToken).ConfigureAwait(false);
        var screenOn = ParseScreenOn(power.StandardOutput);

        var info = AdbOutputParser.BuildDeviceInfo(devices, properties, batteryLevel, screenOn, adbVersion);
        _logger.LogInformation("ADB probe: state={State} model={Model} hyperos={HyperOs}",
            info.State, info.DisplayModel, info.HyperOsVersion ?? "-");
        return info;
    }

    private static int? ParseBatteryLevel(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("level:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed["level:".Length..].Trim();
            return int.TryParse(value, out var level) ? level : null;
        }

        return null;
    }

    private static bool? ParseScreenOn(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("mWakefulness=", StringComparison.Ordinal))
            {
                return trimmed.Contains("Awake", StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    public Task<AdbCommandResult> ExecuteAsync(string arguments, CancellationToken cancellationToken = default) =>
        RunAsync(arguments, cancellationToken);

    public async Task<byte[]?> ExecuteBinaryAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(_executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "adb executable not found at {Path}.", _executable);
            return null;
        }

        using var buffer = new MemoryStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, cts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }

        return process.ExitCode == 0 ? buffer.ToArray() : null;
    }

    private async Task<AdbCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return AdbCommandResult.Failure("Failed to start adb.");
            }
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "adb executable not found at {Path}.", _executable);
            return AdbCommandResult.Failure($"adb not found: {ex.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return AdbCommandResult.Failure($"adb command timed out after {DefaultTimeout.TotalSeconds:0}s: adb {arguments}");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new AdbCommandResult(process.ExitCode == 0, stdout, stderr, process.ExitCode);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    public async Task<bool> LaunchAppAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            $"shell monkey -p {packageId} -c android.intent.category.LAUNCHER 1", cancellationToken)
            .ConfigureAwait(false);
        return result.Ok && !result.StandardOutput.Contains("No activities found", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> TapAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync($"shell input tap {x} {y}", cancellationToken).ConfigureAwait(false);
        return result.Ok;
    }

    public async Task<bool> IsInstalledAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync($"shell pm list packages {packageId}", cancellationToken).ConfigureAwait(false);
        return result.Ok && result.StandardOutput.Contains(packageId, StringComparison.Ordinal);
    }

    public async Task<string?> GetPackageVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync($"shell dumpsys package {packageId}", cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return null;
        }

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("versionName=", StringComparison.Ordinal))
            {
                return trimmed["versionName=".Length..].Trim();
            }
        }

        return null;
    }
}
