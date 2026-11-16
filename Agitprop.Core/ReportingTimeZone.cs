namespace Agitprop.Core;

/// <summary>
/// The calendar the project reports in. The outlets publish Budapest wall-clock time and the
/// dashboard is read by a Hungarian audience, so both the scraped timestamps and the day buckets
/// are anchored here — never to the machine that happens to run the code.
/// Instants are still stored and compared in UTC.
/// </summary>
public static class ReportingTimeZone
{
    private const string IanaId = "Europe/Budapest";
    private const string WindowsId = "Central Europe Standard Time";

    private static readonly TimeZoneInfo Zone = ResolveZone();

    /// <summary>Reads a UTC instant as the day it falls on in the reporting calendar.</summary>
    public static DateOnly ToDay(DateTime utcInstant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcInstant), Zone));

    /// <summary>Converts a wall-clock time in the reporting calendar to UTC, resolving the DST gap and overlap.</summary>
    public static DateTime ToUtc(DateTime wallClock)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(local))
        {
            // Spring forward skipped this wall clock; move past the gap using its actual size.
            var gap = Zone.GetUtcOffset(local.AddDays(1)) - Zone.GetUtcOffset(local.AddDays(-1));
            var shifted = local + gap;
            return TimeZoneInfo.ConvertTimeToUtc(Zone.IsInvalidTime(shifted) ? local.AddHours(1) : shifted, Zone);
        }

        if (Zone.IsAmbiguousTime(local))
        {
            // Fall back repeats this wall clock; take the first occurrence, which uses the larger offset.
            return DateTime.SpecifyKind(local - Zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }

    /// <summary>The UTC instant at which the given reporting day starts.</summary>
    public static DateTime StartOfDayUtc(DateOnly day) =>
        ToUtc(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// Converts an inclusive reporting-day range into UTC bounds. The upper bound is the last tick of
    /// the end day, so callers can keep using an inclusive comparison.
    /// </summary>
    public static (DateTime From, DateTime To) ToUtcRange(DateOnly startDate, DateOnly endDate) =>
        (StartOfDayUtc(startDate), StartOfDayUtc(endDate.AddDays(1)).AddTicks(-1));

    /// <summary>Today in the reporting calendar.</summary>
    public static DateOnly Today() => ToDay(DateTime.UtcNow);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime()
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

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
