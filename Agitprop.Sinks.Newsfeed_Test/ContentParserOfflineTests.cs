using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserOfflineTests
{
    [Test]
    public async Task RtlParser_ParsesArticleWhenOnlyLeadIsPresent()
    {
        const string html = """
            <meta property="og:video:release_date" content="2026-10-17T20:15:00+00:00">
            <h1 class="page-layout__title">Test RTL headline</h1>
            <div class="static-page__content static-page__content--lead">
                <p>Standalone article lead.</p>
            </div>
            """;

        var parser = TestCaseFactory.GetSource("RTL").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.SourceSite.Value, Is.EqualTo("RTL"));
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2026, 10, 17, 20, 15, 0, DateTimeKind.Utc)));
            Assert.That(result.Title, Is.EqualTo("Test RTL headline"));
            Assert.That(result.Text, Is.EqualTo("Test RTL headline Standalone article lead."));
        });
    }

    [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetContentParserOfflineCases))]
    public void ContentParserTest(SourceId site, ContentParserTestCase testCase, string htmlPath)
    {
        var scraper = TestCaseFactory.GetSource(site).CreateContentParser();

        var htmlContent = File.ReadAllText(htmlPath);
        var result = scraper.ParseContentAsync(htmlContent).Result;

        Assert.Multiple(() =>
        {
            Assert.That(result.SourceSite, Is.EqualTo(testCase.ExpectedContent.SourceSite));
            Assert.That(result.PublishDate, Is.EqualTo(testCase.ExpectedContent.PublishDate));
            Assert.That(result.Text, Is.EqualTo(testCase.ExpectedContent.Text));
        });
    }
}