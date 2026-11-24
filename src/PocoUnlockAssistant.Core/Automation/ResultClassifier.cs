namespace PocoUnlockAssistant.Core.Automation;

public enum FinalResult
{
    /// <summary>An action completed but Xiaomi's answer is not confirmed yet.</summary>
    Submitted,
    Approved,
    Rejected,
    QuotaExhausted,
    AccountIneligible,
    NetworkError,
    SecurityChallenge,
    Unknown,
}

public sealed record Classification(
    FinalResult Result,
    ConfidenceLevel Confidence,
    string Evidence,
    ScreenKind Screen)
{
    /// <summary>
    /// An ambiguous outcome must never be retried automatically, because a retry can
    /// burn the daily quota or create a duplicate authorization request.
    /// </summary>
    public bool IsAmbiguous => Result == FinalResult.Unknown || Confidence == ConfidenceLevel.Low;
}

/// <summary>
/// Turns an observed screen into a result category. Deliberately conservative:
/// a tap alone is never reported as success, and unrecognized screens stay UNKNOWN.
/// </summary>
public static class ResultClassifier
{
    private static readonly string[] ExplicitApproval =
        ["approved", "disetujui", "granted", "permission granted", "izin diberikan", "you can unlock"];

    private static readonly string[] ExplicitRejection =
        ["rejected", "ditolak", "denied", "not approved", "tidak disetujui"];

    private static readonly string[] AmbiguousSuccess =
        ["berhasil", "success", "submitted", "terkirim", "dikirim"];

    public static Classification Classify(IReadOnlyList<UiNode> nodes, ScreenRecognitionProfile profile)
    {
        var match = ScreenRecognizer.Evaluate(nodes, profile);
        var texts = nodes.Select(n => n.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        var haystack = string.Join(" | ", texts).ToLowerInvariant();

        switch (match.Kind)
        {
            case ScreenKind.SecurityChallenge:
                return new(FinalResult.SecurityChallenge, ConfidenceLevel.High,
                    "Security challenge (CAPTCHA / verification) is on screen; automation stopped.", match.Kind);

            case ScreenKind.ResultQuota:
                return new(FinalResult.QuotaExhausted, ConfidenceLevel.High,
                    "Quota message detected; do not retry today.", match.Kind);

            case ScreenKind.ResultAccount:
                return new(FinalResult.AccountIneligible, ConfidenceLevel.Medium,
                    "Account eligibility message detected; check account status in Xiaomi Community.", match.Kind);

            case ScreenKind.ResultNetwork:
                return new(FinalResult.NetworkError, ConfidenceLevel.Medium,
                    "Network error message detected.", match.Kind);

            case ScreenKind.ResultRejected:
                return new(FinalResult.Rejected, ConfidenceLevel.Medium,
                    "Explicit rejection text detected.", match.Kind);

            case ScreenKind.ResultSuccess:
                return ClassifySuccess(haystack, match);

            case ScreenKind.UnlockBootloader:
            case ScreenKind.SubmissionReady:
                return new(FinalResult.Unknown, ConfidenceLevel.Low,
                    "The unlock screen is still visible; no response has been confirmed.", match.Kind);

            default:
                return new(FinalResult.Unknown, ConfidenceLevel.Low,
                    match.Evidence.Count > 0
                        ? string.Join("; ", match.Evidence)
                        : "Unrecognized screen; result cannot be verified.",
                    match.Kind);
        }
    }

    private static Classification ClassifySuccess(string haystack, ScreenMatch match)
    {
        if (Contains(haystack, ExplicitRejection))
        {
            return new(FinalResult.Rejected, ConfidenceLevel.Medium,
                "Explicit rejection text detected.", match.Kind);
        }

        if (Contains(haystack, ExplicitApproval))
        {
            return new(FinalResult.Approved, ConfidenceLevel.High,
                "Explicit approval text detected in the Xiaomi Community screen.", match.Kind);
        }

        if (Contains(haystack, AmbiguousSuccess))
        {
            return new(FinalResult.Submitted, ConfidenceLevel.Medium,
                "Submission acknowledgement detected, but approval is not confirmed.", match.Kind);
        }

        return new(FinalResult.Unknown, ConfidenceLevel.Low,
            "Success screen matched without explicit outcome text.", match.Kind);
    }

    private static bool Contains(string haystack, IEnumerable<string> needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}
