using System.Text.Json;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Configuration;

namespace PocoUnlockAssistant.Infrastructure.Configuration;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public string Path => _path;

    public AssistantSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AssistantSettings();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AssistantSettings>(json, Options) ?? new AssistantSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AssistantSettings();
        }
    }

    public void Save(AssistantSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>Loads the configurable screen-recognition profile, writing the default one on first run.</summary>
    public ScreenRecognitionProfile LoadScreenProfile(string? path = null)
    {
        var target = path ?? AppPaths.ScreenProfileFile;
        if (File.Exists(target))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<ScreenRecognitionProfile>(
                    File.ReadAllText(target), Options);
                if (profile is not null && profile.Markers.Count > 0)
                {
                    return profile;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Fall through to the default profile.
            }
        }

        var defaultProfile = ScreenRecognitionProfile.CreateDefault();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, JsonSerializer.Serialize(defaultProfile, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only location: the in-memory default still works.
        }

        return defaultProfile;
    }
}
