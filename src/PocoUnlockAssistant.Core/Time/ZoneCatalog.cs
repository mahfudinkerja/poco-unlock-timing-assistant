namespace PocoUnlockAssistant.Core.Time;

/// <summary>
/// Fixed UTC offsets for the zones this tool cares about. China and Indonesia
/// do not observe daylight saving time, so these offsets are exact year-round.
/// The IANA identifiers are still resolved through <see cref="TimeZoneInfo"/>
/// where the host provides them, and fall back to the fixed offset otherwise.
/// </summary>
public static class ZoneCatalog
{
    public const string BeijingId = "Asia/Shanghai";
    public const string JakartaId = "Asia/Jakarta";
    public const string WitaId = "Asia/Makassar";
    public const string WitId = "Asia/Jayapura";

    public static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    public static readonly TimeSpan JakartaOffset = TimeSpan.FromHours(7);
    public static readonly TimeSpan WitaOffset = TimeSpan.FromHours(8);
    public static readonly TimeSpan WitOffset = TimeSpan.FromHours(9);

    private static readonly Dictionary<string, TimeSpan> Fallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        [BeijingId] = BeijingOffset,
        [JakartaId] = JakartaOffset,
        [WitaId] = WitaOffset,
        [WitId] = WitOffset,
    };

    /// <summary>Returns the zone offset that applies to <paramref name="instant"/>.</summary>
    public static TimeSpan ResolveOffset(string zoneId, DateTimeOffset instant)
    {
        if (Fallbacks.TryGetValue(zoneId, out var fallback))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                return zone.GetUtcOffset(instant.UtcDateTime);
            }
            catch (TimeZoneNotFoundException)
            {
                return fallback;
            }
            catch (InvalidTimeZoneException)
            {
                return fallback;
            }
        }

        throw new ArgumentException($"Unknown zone '{zoneId}'.", nameof(zoneId));
    }

    public static TimeSpan Beijing(DateTimeOffset instant) => ResolveOffset(BeijingId, instant);

    public static TimeSpan Jakarta(DateTimeOffset instant) => ResolveOffset(JakartaId, instant);
}
