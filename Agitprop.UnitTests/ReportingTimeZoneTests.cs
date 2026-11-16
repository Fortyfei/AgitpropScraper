using Agitprop.Core;
using Agitprop.Sources.Scrapers.ContentParsers;

namespace Agitprop.UnitTests;

public sealed class ReportingTimeZoneTests
{
    [Test]
    public void ToDay_ReadsTheUtcInstantInTheReportingCalendar()
    {
        // 2025-01-11 23:30 UTC is already 2025-01-12 00:30 in Budapest.
        Assert.That(ReportingTimeZone.ToDay(new DateTime(2025, 1, 11, 23, 30, 0, DateTimeKind.Utc)),
            Is.EqualTo(new DateOnly(2025, 1, 12)));
    }

    [Test]
    public void ToDay_UsesTheSummerOffset()
    {
        // July is CEST (UTC+2), so 22:30 UTC is already the next day.
        Assert.That(ReportingTimeZone.ToDay(new DateTime(2025, 7, 10, 22, 30, 0, DateTimeKind.Utc)),
            Is.EqualTo(new DateOnly(2025, 7, 11)));
    }

    [Test]
    public void ToUtc_ResolvesWinterWallClockAgainstBudapest()
    {
        var utc = ReportingTimeZone.ToUtc(new DateTime(2025, 1, 11, 17, 14, 30));

        Assert.Multiple(() =>
        {
            Assert.That(utc, Is.EqualTo(new DateTime(2025, 1, 11, 16, 14, 30)));
            Assert.That(utc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void ToUtc_ResolvesSummerWallClockAgainstBudapest() =>
        Assert.That(ReportingTimeZone.ToUtc(new DateTime(2025, 7, 10, 17, 14, 30)),
            Is.EqualTo(new DateTime(2025, 7, 10, 15, 14, 30)));

    [Test]
    public void ToUtc_MovesClockPastTheSpringForwardGap()
    {
        // 2025-03-30 02:30 never happens in Budapest; it is read as 03:30 CEST.
        Assert.That(ReportingTimeZone.ToUtc(new DateTime(2025, 3, 30, 2, 30, 0)),
            Is.EqualTo(new DateTime(2025, 3, 30, 1, 30, 0)));
    }

    [Test]
    public void ToUtc_UsesTheFirstOccurrenceOfAnAmbiguousFallBackTime()
    {
        // 2025-10-26 02:30 happens twice; the first occurrence is still CEST (UTC+2).
        Assert.That(ReportingTimeZone.ToUtc(new DateTime(2025, 10, 26, 2, 30, 0)),
            Is.EqualTo(new DateTime(2025, 10, 26, 0, 30, 0)));
    }

    [Test]
    public void StartOfDayUtc_StartsTheDayInTheReportingCalendar()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReportingTimeZone.StartOfDayUtc(new DateOnly(2025, 1, 12)),
                Is.EqualTo(new DateTime(2025, 1, 11, 23, 0, 0)));
            Assert.That(ReportingTimeZone.StartOfDayUtc(new DateOnly(2025, 7, 11)),
                Is.EqualTo(new DateTime(2025, 7, 10, 22, 0, 0)));
        });
    }

    [Test]
    public void ToUtcRange_CoversWholeReportingDays()
    {
        var (from, to) = ReportingTimeZone.ToUtcRange(new DateOnly(2025, 1, 12), new DateOnly(2025, 1, 12));

        Assert.Multiple(() =>
        {
            Assert.That(from, Is.EqualTo(new DateTime(2025, 1, 11, 23, 0, 0)),
                "the day starts at Budapest midnight");
            Assert.That(to.AddTicks(1), Is.EqualTo(new DateTime(2025, 1, 12, 23, 0, 0)),
                "the range ends exactly where the next reporting day starts");
            Assert.That(ReportingTimeZone.ToDay(new DateTime(2025, 1, 11, 23, 30, 0)), Is.EqualTo(new DateOnly(2025, 1, 12)),
                "an article just after local midnight belongs to the reporting day, and the range includes it");
            Assert.That(from <= new DateTime(2025, 1, 11, 23, 30, 0) && new DateTime(2025, 1, 11, 23, 30, 0) <= to,
                Is.True);
        });
    }

    [TestCase("2025-01-11T17:14:30.0000000Z", "2025-01-11T17:14:30Z")]
    [TestCase("2025-01-11T17:14:30.4968228+01:00", "2025-01-11T16:14:30.4968228Z")]
    [TestCase("2025-01-11T17:14:30+0100", "2025-01-11T16:14:30Z")]
    [TestCase("2025-01-11T17:14:30.0000000", "2025-01-11T16:14:30Z")]
    [TestCase("2025-01-11", "2025-01-10T23:00:00Z")]
    [TestCase("2025. 01. 11.", "2025-01-10T23:00:00Z")]
    [TestCase("2025-01-11 17:14", "2025-01-11T16:14:00Z")]
    public async Task Parser_ResolvesEveryShapeToTheSameUtcInstant(string value, string expectedUtc)
    {
        var parser = new HvgArticleContentParser();

        var result = await parser.ParseContentAsync(
            $"""
            <html><body>
              <h1 class="title">Cím</h1>
              <meta property="article:published_time" content="{value}" />
              <div id="free-body"><p>Első bekezdés.</p></div>
            </body></html>
            """);

        var expected = DateTime.Parse(
            expectedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal);

        Assert.That(result.PublishDate, Is.EqualTo(expected), $"value '{value}'");
        Assert.That(result.PublishDate.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task Parser_AnchorsOffsetLessValuesToTheReportingTimezone()
    {
        var parser = new HvgArticleContentParser();

        var result = await parser.ParseContentAsync(
            """
            <html><body>
              <h1 class="title">Cím</h1>
              <meta property="article:published_time" content="2025-07-10T17:14:30.0000000" />
              <div id="free-body"><p>Első bekezdés.</p></div>
            </body></html>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2025, 7, 10, 15, 14, 30)));
            Assert.That(result.PublishDate.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }
}
