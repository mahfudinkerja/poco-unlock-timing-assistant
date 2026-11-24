using System.ComponentModel;
using System.Diagnostics;

namespace PocoUnlockAssistant.Infrastructure.Adb;

/// <summary>
/// Read-only fastboot detection used by the Mi Unlock Tool section. It never flashes,
/// unlocks or otherwise modifies the device.
/// </summary>
public sealed class FastbootClient
{
    private readonly string _executable;

    public FastbootClient(string? fastbootPath = null)
    {
        _executable = string.IsNullOrWhiteSpace(fastbootPath)
            ? (OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot")
            : fastbootPath;
    }

    public bool TryListDevices(out string output, out string? error)
    {
        output = string.Empty;
        error = null;

        try
        {
            var startInfo = new ProcessStartInfo(_executable, "devices")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "fastboot could not be started.";
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = $"fastboot not available: {ex.Message}";
            return false;
        }
    }
}
