namespace PocoUnlockAssistant.Core.Device;

public enum AdbConnectionState
{
    /// <summary>adb.exe itself could not be executed.</summary>
    AdbUnavailable,
    /// <summary>No device in the device list.</summary>
    NotAttached,
    /// <summary>USB debugging authorization prompt not accepted.</summary>
    Unauthorized,
    /// <summary>Device listed but not usable (offline / booting / no permissions).</summary>
    Offline,
    /// <summary>Device is attached, authorized and ready for commands.</summary>
    Ready,
}

public sealed record AdbDeviceEntry(string Serial, string State, bool IsUsb, string? Model)
{
    public bool IsAuthorized => State.Equals("device", StringComparison.OrdinalIgnoreCase);
}

public sealed record AdbDeviceInfo
{
    public bool AdbAvailable { get; init; }
    public AdbConnectionState State { get; init; } = AdbConnectionState.NotAttached;
    public IReadOnlyList<AdbDeviceEntry> Devices { get; init; } = [];

    public string? Model { get; init; }
    public string? MarketName { get; init; }
    public string? AndroidVersion { get; init; }
    public string? HyperOsVersion { get; init; }
    public string? SerialNumber { get; init; }
    public string? UsbState { get; init; }
    public int? BatteryLevel { get; init; }
    public bool? ScreenOn { get; init; }
    public string? AdbVersion { get; init; }
    public string? Error { get; init; }

    public string DisplayModel =>
        !string.IsNullOrWhiteSpace(MarketName) ? MarketName!
        : !string.IsNullOrWhiteSpace(Model) ? Model!
        : "Unknown device";

    public bool IsReady => State == AdbConnectionState.Ready;

    public string StateLabel => State switch
    {
        AdbConnectionState.AdbUnavailable => "ADB not found",
        AdbConnectionState.NotAttached => "No device",
        AdbConnectionState.Unauthorized => "Unauthorized",
        AdbConnectionState.Offline => "Offline",
        AdbConnectionState.Ready => "Ready",
        _ => "Unknown",
    };

    public static AdbDeviceInfo Unavailable(string reason) =>
        new() { AdbAvailable = false, State = AdbConnectionState.AdbUnavailable, Error = reason };
}

/// <summary>Parses raw adb output. Kept pure so it can be unit-tested without a device.</summary>
public static class AdbOutputParser
{
    public static IReadOnlyList<AdbDeviceEntry> ParseDeviceList(string stdout)
    {
        var entries = new List<AdbDeviceEntry>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            // adb separates fields with tabs, but some builds use spaces; split on any whitespace.
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = parts[1];
            var isUsb = false;
            string? model = null;

            for (var i = 2; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("usb:", StringComparison.OrdinalIgnoreCase))
                {
                    isUsb = true;
                }
                else if (parts[i].StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                {
                    model = parts[i]["model:".Length..].Replace('_', ' ');
                }
            }

            entries.Add(new AdbDeviceEntry(serial, state, isUsb, model));
        }

        return entries;
    }

    /// <summary>Parses <c>adb shell getprop</c> output of the form <c>[key]: [value]</c>.</summary>
    public static Dictionary<string, string> ParseProperties(string stdout)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('['))
            {
                continue;
            }

            var close = line.IndexOf("]: [", StringComparison.Ordinal);
            if (close < 1)
            {
                continue;
            }

            var key = line[1..close];
            var valueStart = close + 4;
            var valueEnd = line.LastIndexOf(']');
            if (valueEnd <= valueStart)
            {
                continue;
            }

            properties[key] = line[valueStart..valueEnd];
        }

        return properties;
    }

    public static AdbDeviceInfo BuildDeviceInfo(
        IReadOnlyList<AdbDeviceEntry> devices,
        IReadOnlyDictionary<string, string> properties,
        int? batteryLevel,
        bool? screenOn,
        string? adbVersion)
    {
        var primary = devices.FirstOrDefault(d => d.IsAuthorized) ?? devices.FirstOrDefault();

        var state = primary is null
            ? AdbConnectionState.NotAttached
            : primary.State.ToLowerInvariant() switch
            {
                "device" => AdbConnectionState.Ready,
                "unauthorized" => AdbConnectionState.Unauthorized,
                _ => AdbConnectionState.Offline,
            };

        return new AdbDeviceInfo
        {
            AdbAvailable = true,
            State = state,
            Devices = devices,
            Model = Get(properties, "ro.product.model"),
            MarketName = Get(properties, "ro.product.marketname") ?? primary?.Model,
            AndroidVersion = Get(properties, "ro.build.version.release"),
            HyperOsVersion =
                Get(properties, "ro.mi.os.version.incremental")
                ?? Get(properties, "ro.mi.os.version.name")
                ?? Get(properties, "ro.build.version.incremental"),
            SerialNumber = Get(properties, "ro.serialno") ?? primary?.Serial,
            UsbState = Get(properties, "sys.usb.state"),
            BatteryLevel = batteryLevel,
            ScreenOn = screenOn,
            AdbVersion = adbVersion,
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
