using Agitprop.Sources.Scrapers.ContentParsers;

using HtmlAgilityPack;

namespace Agitprop.UnitTests;

public sealed class SourceTimeZoneTests
{
    [Test]
    public void ToUtc_ResolvesWinterWallClockAgainstBudapest()
    {
        var utc = SourceTimeZone.ToUtc(new DateTime(2025, 1, 11, 17, 14, 30));

        Assert.Multiple(() =>
        {
            Assert.That(utc, Is.EqualTo(new DateTime(2025, 1, 11, 16, 14, 30)));
            Assert.That(utc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void ToUtc_ResolvesSummerWallClockAgainstBudapest()
    {
        var utc = SourceTimeZone.ToUtc(new DateTime(2025, 7, 10, 17, 14, 30));

        Assert.That(utc, Is.EqualTo(new DateTime(2025, 7, 10, 15, 14, 30)));
    }

    [Test]
    public void ToUtc_MovesClockPastTheSpringForwardGap()
    {
        // 2025-03-30 02:30 never happens in Budapest; it is read as 03:30 CEST.
        var utc = SourceTimeZone.ToUtc(new DateTime(2025, 3, 30, 2, 30, 0));

        Assert.That(utc, Is.EqualTo(new DateTime(2025, 3, 30, 1, 30, 0)));
    }

    [Test]
    public void ToUtc_UsesTheFirstOccurrenceOfAnAmbiguousFallBackTime()
    {
        // 2025-10-26 02:30 happens twice; the first occurrence is still CEST (UTC+2).
        var utc = SourceTimeZone.ToUtc(new DateTime(2025, 10, 26, 2, 30, 0));

        Assert.That(utc, Is.EqualTo(new DateTime(2025, 10, 26, 0, 30, 0)));
    }

    [TestCase("2025-01-11T17:14:30.0000000Z", true)]
    [TestCase("2025-01-11T17:14:30.4968228+01:00", true)]
    [TestCase("2025-01-11T17:14:30+0100", true)]
    [TestCase("2025-01-11T17:14:30.0000000", false)]
    [TestCase("2025-01-11", false)]
    [TestCase("2025. 01. 11.", false)]
    [TestCase("2025-01-11 17:14", false)]
    public void HasExplicitOffset_OnlyMatchesValuesThatCarryAZone(string value, bool expected) =>
        Assert.That(SourceTimeZone.HasExplicitOffset(value), Is.EqualTo(expected));

    [Test]
    public async Task Parser_HonoursAnExplicitOffset()
    {
        var parser = new HvgArticleContentParser();

        var result = await parser.ParseContentAsync(
            """
            <html><body>
              <h1 class="title">Cím</h1>
              <meta property="article:published_time" content="2025-01-11T17:14:30.0000000Z" />
              <div id="free-body"><p>Első bekezdés.</p></div>
            </body></html>
            """);

        Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2025, 1, 11, 17, 14, 30)));
    }

    [Test]
    public async Task Parser_AnchorsOffsetLessValuesToTheOutletTimezone()
    {
        var parser = new HvgArticleContentParser();

        var result = await parser.ParseContentAsync(
            """
            <html><body>
              <h1 class="title">Cím</h1>
              <meta property="article:published_time" content="2025-01-11T17:14:30.0000000" />
              <div id="free-body"><p>Első bekezdés.</p></div>
            </body></html>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2025, 1, 11, 16, 14, 30)));
            Assert.That(result.PublishDate.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public async Task Parser_ParsingIsIndependentOfTheHostTimezone()
    {
        var parser = new HvgArticleContentParser();
        const string html =
            """
            <html><body>
              <h1 class="title">Cím</h1>
              <meta property="article:published_time" content="2025-07-10T17:14:30.0000000" />
              <div id="free-body"><p>Első bekezdés.</p></div>
            </body></html>
            """;

        var first = await parser.ParseContentAsync(html);
        var second = await parser.ParseContentAsync(html);

        Assert.That(second.PublishDate, Is.EqualTo(first.PublishDate));
        Assert.That(first.PublishDate, Is.EqualTo(new DateTime(2025, 7, 10, 15, 14, 30)));
    }
}
