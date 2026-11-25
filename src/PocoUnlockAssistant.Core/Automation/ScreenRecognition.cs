namespace PocoUnlockAssistant.Core.Automation;

public enum ScreenKind
{
    Unknown,
    Home,
    MeTab,
    UnlockBootloader,
    SubmissionReady,
    SecurityChallenge,
    ResultSuccess,
    ResultQuota,
    ResultAccount,
    ResultRejected,
    ResultNetwork,
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High,
}

public readonly record struct UiBounds(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>A normalized node from the UIAutomator hierarchy dump.</summary>
public sealed record UiNode(
    string Text,
    string ResourceId,
    string ContentDescription,
    string ClassName,
    bool Enabled,
    bool Clickable,
    UiBounds Bounds)
{
    public string Combined => $"{Text} {ContentDescription} {ResourceId}".ToLowerInvariant();
}

public sealed record ScreenMarker(ScreenKind Kind, IReadOnlyList<string> Texts);

public sealed class ScreenRecognitionProfile
{
    public string Name { get; set; } = "default";
    public string PackageId { get; set; } = "com.mi.global.bbs";
    public string Notes { get; set; } = string.Empty;
    public List<ScreenMarker> Markers { get; set; } = [];
    public List<string> SubmitButtonTexts { get; set; } = [];
    public List<string> SubmitButtonIds { get; set; } = [];
    public TimeSpan RecognitionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ships with the strings observed in the official Xiaomi Community app for both the
    /// English and Indonesian locales. The package id and hierarchy must still be confirmed
    /// on the target device (Diagnostics → Inspect screen), because Xiaomi changes them.
    /// </summary>
    public static ScreenRecognitionProfile CreateDefault() => new()
    {
        Name = "xiaomi-community-default",
        PackageId = "com.mi.global.bbs",
        Notes = "Verify package id and screen markers on the target POCO F7 before arming automation.",
        Markers =
        [
            new(ScreenKind.SecurityChallenge,
            [
                "captcha", "verify", "verifikasi", "security check", "pemeriksaan keamanan",
                "slide to", "geser", "human", "manusia", "additional verification", "verifikasi tambahan",
            ]),
            new(ScreenKind.ResultQuota,
            [
                "quota", "kuota", "limit reached", "batas tercapai", "try again tomorrow",
                "coba lagi besok", "daily limit", "exceeded",
            ]),
            new(ScreenKind.ResultAccount,
            [
                "not eligible", "tidak memenuhi syarat", "restricted", "dibatasi", "ineligible",
                "account requirement", "persyaratan akun", "168 hours", "30 days", "30 hari",
            ]),
            new(ScreenKind.ResultRejected,
            [
                "rejected", "ditolak", "denied", "not approved", "tidak disetujui",
                "request failed", "permintaan gagal",
            ]),
            new(ScreenKind.ResultSuccess,
            [
                "approved", "disetujui", "granted", "berhasil", "success", "you can unlock",
                "unlock permission", "izin unlock",
            ]),
            new(ScreenKind.ResultNetwork,
            [
                "network error", "kesalahan jaringan", "timeout", "waktu habis", "connection failed",
                "koneksi gagal", "no internet", "tidak ada koneksi", "server error", "try again",
            ]),
            new(ScreenKind.UnlockBootloader,
            [
                "unlock bootloader", "buka bootloader", "bootloader unlock", "mi unlock status",
                "status unlock", "unlock requirement",
            ]),
            new(ScreenKind.MeTab, ["my account", "akun saya", "profile tab", "tab profil"]),
            new(ScreenKind.Home, ["community feed", "beranda komunitas"]),
        ],
        SubmitButtonTexts =
        [
            "apply", "ajukan", "request", "minta", "unlock", "buka", "submit", "kirim", "confirm", "konfirmasi",
        ],
        SubmitButtonIds = ["com.mi.global.bbs:id/", "com.miui.community:id/", "button", "submit", "apply"],
    };
}

public sealed record ScreenMatch(ScreenKind Kind, ConfidenceLevel Confidence, IReadOnlyList<string> Evidence)
{
    public static ScreenMatch Unknown(params string[] evidence) =>
        new(ScreenKind.Unknown, ConfidenceLevel.Low, evidence);
}

/// <summary>
/// Scores the visible accessibility hierarchy against the recognition profile.
/// Pure and deterministic so that simulated screens can be unit-tested.
/// </summary>
public static class ScreenRecognizer
{
    public static ScreenMatch Evaluate(IReadOnlyList<UiNode> nodes, ScreenRecognitionProfile profile)
    {
        if (nodes.Count == 0)
        {
            return ScreenMatch.Unknown("Empty UI hierarchy.");
        }

        ScreenMatch? best = null;
        var bestScore = 0;

        foreach (var marker in profile.Markers)
        {
            var hits = new List<string>();
            foreach (var candidate in marker.Texts)
            {
                var needle = candidate.ToLowerInvariant();
                if (nodes.Any(n => n.Combined.Contains(needle, StringComparison.Ordinal) ||
                                   n.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                {
                    hits.Add(candidate);
                }
            }

            if (hits.Count == 0 || hits.Count < bestScore)
            {
                continue;
            }

            if (hits.Count == bestScore && best is not null)
            {
                continue;
            }

            bestScore = hits.Count;
            best = new ScreenMatch(
                marker.Kind,
                hits.Count >= 2 ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                hits);
        }

        if (best is null)
        {
            return ScreenMatch.Unknown($"No profile marker matched {nodes.Count} visible nodes.");
        }

        if (best.Kind == ScreenKind.UnlockBootloader && FindSubmitButton(nodes, profile) is { } button)
        {
            var evidence = best.Evidence.Concat([$"button:{button.Text}"]).ToArray();
            return new ScreenMatch(
                ScreenKind.SubmissionReady,
                button.Enabled ? ConfidenceLevel.High : ConfidenceLevel.Low,
                evidence);
        }

        return best;
    }

    public static UiNode? FindSubmitButton(IReadOnlyList<UiNode> nodes, ScreenRecognitionProfile profile)
    {
        // A disabled button is still evidence of the right screen; the gate refuses to tap it.
        foreach (var node in nodes.Where(n => n.Clickable))
        {
            if (profile.SubmitButtonTexts.Any(t =>
                    node.Text.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    node.ContentDescription.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                return node;
            }

            if (profile.SubmitButtonIds.Any(id =>
                    node.ResourceId.Contains(id, StringComparison.OrdinalIgnoreCase)))
            {
                return node;
            }
        }

        return null;
    }
}
