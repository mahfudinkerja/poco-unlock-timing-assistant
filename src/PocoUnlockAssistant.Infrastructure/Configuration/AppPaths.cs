namespace PocoUnlockAssistant.Infrastructure.Configuration;

/// <summary>Resolves the writable data directory used for settings, logs, evidence and the audit DB.</summary>
public static class AppPaths
{
    public const string ProductFolder = "PocoUnlockAssistant";

    public static string Root { get; } = ResolveRoot();

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string DatabaseFile => Path.Combine(Root, "attempts.db");

    public static string EvidenceDirectory => Path.Combine(Root, "evidence");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string ScreenProfileFile => Path.Combine(Root, "screen-profile.json");

    private static string ResolveRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(appData, ProductFolder);
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(EvidenceDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
