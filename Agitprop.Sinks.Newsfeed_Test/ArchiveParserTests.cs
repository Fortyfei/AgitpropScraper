using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;

public class ArchiveParserTests
{
    [SetUp]
    public void Setup()
    {
    }

    //[TestCase(NewsSites.Alfahir, 10)]
    [TestCase(NewsSites.HVG, 157)] //TODO: ez is letekerős, mint a 444
    [TestCase(NewsSites.Index, 3437)]
    [TestCase(NewsSites.MagyarJelen, 8)]
    [TestCase(NewsSites.MagyarNemzet, 4062)]
    [TestCase(NewsSites.Mandiner, 3103)]
    [TestCase(NewsSites.Merce, 3)]
    [TestCase(NewsSites.Metropol, 1689)]
    [TestCase(NewsSites.Origo, 100)]
    [TestCase(NewsSites.PestiSracok, 45)] //ha több oldal van, akkor kell paginator
    [TestCase(NewsSites.Ripost, 1887)]
    [TestCase(NewsSites.RTL, 50)]
    [TestCase(NewsSites.Telex, 85)]
    [TestCase(NewsSites.HuszonnegyHu, 24)]
    [TestCase(NewsSites.NegyNegyNegy, 55)]
    public void ArchiveParserTest(NewsSites siteIn, int expectedCount)
    {
        var parser = ArchiveLinkParserFactory.GetLinkParser(siteIn);
        var htmlContent = File.ReadAllText(TestCaseFactory.GetArchiveParserTestCasePath(siteIn));
        var result = parser.GetLinksAsync("testBaseUrl", htmlContent).Result;
        Assert.That(result, Has.Count.EqualTo(expectedCount));
    }
}
