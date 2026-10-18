using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;
public class ContentParserOfflineTests
{
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