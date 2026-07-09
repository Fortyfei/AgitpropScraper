using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;
public partial class ContentParserOfflineTests
{
    [TestCase(NewsSites.Alfahir)]
    [TestCase(NewsSites.HVG)]
    [TestCase(NewsSites.Index)]
    [TestCase(NewsSites.MagyarJelen)]
    [TestCase(NewsSites.MagyarNemzet)]
    [TestCase(NewsSites.Mandiner)]
    [TestCase(NewsSites.Merce)]
    [TestCase(NewsSites.Metropol)]
    [TestCase(NewsSites.Origo)]
    [TestCase(NewsSites.PestiSracok)]
    [TestCase(NewsSites.Ripost)]
    [TestCase(NewsSites.RTL)]
    [TestCase(NewsSites.Telex)]
    [TestCase(NewsSites.HuszonnegyHu)]
    [TestCase(NewsSites.NegyNegyNegy)]
    public void ContentParserTest(NewsSites site)
    {
        var scraper = ContentParserFactory.GetContentParser(site);
        foreach (var testCase in TestCaseFactory.GetContentParserTestCases(site))
        {
            foreach (var htmlPath in testCase.GetHtmlPaths())
            {
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
    }
}