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

    [Test]
    public async Task RtlParser_ParsesCurrentPublishedTimeMetadata()
    {
        const string html = """
            <meta property="og:article:published_time" content="2026-10-18T17:00:00+00:00">
            <h1>RTL article title</h1>
            <div class="static-page__content static-page__content--lead"><p>Article lead.</p></div>
            """;

        var parser = TestCaseFactory.GetSource("RTL").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2026, 10, 18, 17, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.Title, Is.EqualTo("RTL article title"));
            Assert.That(result.Text, Is.EqualTo("RTL article title Article lead."));
        });
    }

    [Test]
    public async Task IndexParser_UsesOpenGraphTitleAndGenericArticleBody()
    {
        const string html = """
            <meta property="article:published_time" content="2026-10-21T16:06:00+02:00">
            <meta property="og:title" content="Index post title">
            <div class="article-content"><p>Index post text.</p></div>
            """;

        var parser = TestCaseFactory.GetSource("Index").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Index post title"));
            Assert.That(result.Text, Is.EqualTo("Index post title Index post text."));
        });
    }

    [Test]
    public async Task IndexParser_UsesPublishedTimeInsteadOfStaleUpdatedTime()
    {
        const string html = """
            <meta property="og:updated_time" content="1970-01-01T01:00:00+01:00">
            <meta property="article:published_time" content="2026-10-24T11:43:00Z">
            <h1>Index article title</h1>
            <div class="cikk-torzs"><p>Index article text.</p></div>
            """;

        var parser = TestCaseFactory.GetSource("Index").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2026, 10, 24, 11, 43, 0, DateTimeKind.Utc)));
            Assert.That(result.Title, Is.EqualTo("Index article title"));
            Assert.That(result.Text, Is.EqualTo("Index article title Index article text."));
        });
    }

    [Test]
    public async Task IndexParser_UsesPublicationDateWhenPublishedTimeIsUnavailable()
    {
        const string html = """
            <meta name="i:publication" content="2026. 10. 25.">
            <h1>Index article title</h1>
            <div class="cikk-torzs"><p>Index article text.</p></div>
            """;

        var parser = TestCaseFactory.GetSource("Index").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate.Date, Is.EqualTo(new DateTime(2026, 10, 25)));
            Assert.That(result.Title, Is.EqualTo("Index article title"));
            Assert.That(result.Text, Is.EqualTo("Index article title Index article text."));
        });
    }

    [Test]
    public async Task IndexParser_ExcludesPublisherPromotionsAndScriptsFromArticleText()
    {
        const string html = """
            <meta property="article:published_time" content="2026-10-24T11:43:00Z">
            <h1>Article title</h1>
            <div class="cikk-torzs">
                <p>The article discusses Index's reporting on this issue.</p>
                <a class="google-pref-banner">Put Index at the top of Google results.</a>
                <script>const publisher = "Index";</script>
                <style>.publisher::after { content: "Index"; }</style>
                <div class="miniapp microsite">
                    <script>const zone = "Index_Belfold_Layer";</script>
                </div>
                <div class="miniapp szerkfotoimage">
                    <p>Photo caption: Neville Chamberlain.</p>
                </div>
                <div class="social-stripe cikk-bottom-box">
                    <p>Follow Index on Facebook.</p>
                </div>
                <div class="pp-list">More from Index.</div>
                <div class="cikk-bottom-text-ad">Promotional content.</div>
            </div>
            """;

        var parser = TestCaseFactory.GetSource("Index").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.That(
            result.Text,
            Is.EqualTo(
                "Article title The article discusses Index's reporting on this issue. Photo caption: Neville Chamberlain."));
    }

    [Test]
    public async Task OrigoParser_UsesStructuredDataForDateAndArticleBody()
    {
        const string html = """
            <h1>Origo article title</h1>
            <script type="application/ld+json">
            {"datePublished":"2026-10-19T08:50:47.000Z","articleBody":"First paragraph. Second paragraph."}
            </script>
            """;

        var parser = TestCaseFactory.GetSource("Origo").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(2026, 10, 19, 8, 50, 47, DateTimeKind.Utc)));
            Assert.That(result.Title, Is.EqualTo("Origo article title"));
            Assert.That(result.Text, Is.EqualTo("Origo article title First paragraph. Second paragraph."));
        });
    }

    [Test]
    public async Task OrigoParser_ParsesParagraphsFromBlockContent()
    {
        const string html = """
            <meta property="article:published_time" content="2026-10-24T20:22:00+02:00">
            <h1 class="article-title">Origo article title</h1>
            <div class="article-lead">Article summary.</div>
            <div class="block-content">
                <p>First article paragraph.</p>
                <figure class="image"></figure>
                <p>Second article paragraph.</p>
            </div>
            """;

        var parser = TestCaseFactory.GetSource("Origo").CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Origo article title"));
            Assert.That(result.Text, Is.EqualTo(
                "Origo article title Article summary. First article paragraph. Second article paragraph."));
        });
    }

    [TestCase("Origo")]
    [TestCase("MagyarNemzet")]
    [TestCase("NegyNegyNegy")]
    [TestCase("Metropol")]
    [TestCase("Ripost")]
    public async Task SiteParsers_UseArticleDescriptionWhenBodyIsUnavailable(string site)
    {
        const string html = """
            <meta property="article:published_time" content="2026-10-24T20:22:00+02:00">
            <meta property="og:title" content="Article title">
            <meta property="og:description" content="A useful article summary.">
            <div class="adult-wrapper">
                <p class="adult-text">This content requires age confirmation.</p>
            </div>
            """;

        var parser = TestCaseFactory.GetSource(site).CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Article title"));
            Assert.That(result.Text, Is.EqualTo("Article title A useful article summary."));
        });
    }

    [TestCase("MagyarNemzet")]
    [TestCase("NegyNegyNegy")]
    [TestCase("Ripost")]
    [TestCase("Metropol")]
    public async Task SiteParsers_FallBackToGenericArticleMarkup(string site)
    {
        const string html = """
            <meta property="article:published_time" content="2026-10-19T08:50:47.000Z">
            <h1>Updated article title</h1>
            <div class="article-body"><p>Updated article text.</p></div>
            """;

        var parser = TestCaseFactory.GetSource(site).CreateContentParser();
        var result = await parser.ParseContentAsync(html);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Updated article title"));
            Assert.That(result.Text, Is.EqualTo("Updated article title Updated article text."));
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