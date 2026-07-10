using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserOfflineTests
{
    [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetContentParserOfflineCases))]
    public void ContentParserTest(NewsSites site, ContentParserTestCase testCase, string htmlPath)
    {
        var scraper = ContentParserFactory.GetContentParser(site);

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