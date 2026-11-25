using System.Text.Json.Serialization;
using PocoUnlockAssistant.Core.Persistence;
using PocoUnlockAssistant.Core.Time;

namespace PocoUnlockAssistant.Core.Configuration;

/// <summary>
/// Persisted operator settings. Defaults match the fixed project configuration:
/// 00:00:00 Asia/Shanghai (23:00 WIB the previous day).
/// Only settings that are actually honoured by the runtime live here.
/// </summary>
public sealed class AssistantSettings
{
    public int TargetHour { get; set; } = 0;
    public int TargetMinute { get; set; } = 0;
    public double MaxUncertaintyMs { get; set; } = 150;
    public int LateGraceMs { get; set; } = 3000;
    public bool EnableEvidenceScreenshots { get; set; } = true;
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.ControlledAdb;
    public string[] NtpServers { get; set; } = ["time.google.com", "time.cloudflare.com", "pool.ntp.org"];
    public string CommunityPackageId { get; set; } = "com.mi.global.bbs";
    public string? AdbPath { get; set; }

    [JsonIgnore]
    public TimeOnly TargetTime => new(TargetHour, TargetMinute);

    [JsonIgnore]
    public TimeSpan MaxUncertainty => TimeSpan.FromMilliseconds(MaxUncertaintyMs);

    [JsonIgnore]
    public TimeSpan LateGrace => TimeSpan.FromMilliseconds(LateGraceMs);
}
