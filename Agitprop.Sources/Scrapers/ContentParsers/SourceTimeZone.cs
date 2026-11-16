using System.Text.RegularExpressions;

namespace Agitprop.Sources.Scrapers.ContentParsers;

/// <summary>
/// Resolves publication timestamps that carry no offset.
/// The outlets publish Budapest wall-clock time without a zone, so the value must be anchored to the
/// outlet's timezone instead of the machine that happens to run the scraper.
/// </summary>
internal static class SourceTimeZone
{
    private const string IanaId = "Europe/Budapest";
    private const string WindowsId = "Central Europe Standard Time";

    private static readonly TimeZoneInfo Zone = ResolveZone();

    // A trailing "Z" or "+01:00"/"+0100". The four digits keep plain dates such as 2025-01-11 unmatched.
    private static readonly Regex ExplicitOffsetPattern = new(
        @"(?:[zZ]|[+-]\d{2}:?\d{2})\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when the value already states its own offset and needs no source timezone.</summary>
    public static bool HasExplicitOffset(string value) => ExplicitOffsetPattern.IsMatch(value.TrimEnd());

    /// <summary>
    /// Converts a wall-clock time in the outlet's timezone to UTC, resolving the DST gap and overlap.
    /// </summary>
    public static DateTime ToUtc(DateTime wallClock)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(local))
        {
            // Spring forward skipped this wall clock; move past the gap using its actual size.
            var gap = Zone.GetUtcOffset(local.AddDays(1)) - Zone.GetUtcOffset(local.AddDays(-1));
            var shifted = local + gap;
            return TimeZoneInfo.ConvertTimeToUtc(
                Zone.IsInvalidTime(shifted) ? local.AddHours(1) : shifted,
                Zone);
        }

        if (Zone.IsAmbiguousTime(local))
        {
            // Fall back repeats this wall clock; take the first occurrence, which uses the larger offset.
            return DateTime.SpecifyKind(local - Zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsId);
        }
    }
}
