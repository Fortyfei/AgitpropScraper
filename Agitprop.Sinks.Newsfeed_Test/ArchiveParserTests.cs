using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.Sinks.Newsfeed_Test;

public class ArchiveParserTests
{
    [SetUp]
    public void Setup()
    {
    }

    [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetArchiveParserCases))]
    public void ArchiveParserTest(NewsSites siteIn, int expectedCount)
    {
        var parser = ArchiveLinkParserFactory.GetLinkParser(siteIn);
        var htmlContent = File.ReadAllText(TestCaseFactory.GetArchiveParserTestCasePath(siteIn));
        var result = parser.GetLinksAsync("testBaseUrl", htmlContent).Result;
        Assert.That(result, Has.Count.EqualTo(expectedCount));
    }
}
