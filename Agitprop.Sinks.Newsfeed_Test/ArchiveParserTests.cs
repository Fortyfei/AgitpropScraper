using Agitprop.Core;

namespace Agitprop.Sinks.Newsfeed_Test;

public class ArchiveParserTests
{
    [SetUp]
    public void Setup()
    {
    }

    [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.GetArchiveParserCases))]
    public void ArchiveParserTest(SourceId siteIn, int expectedCount)
    {
        var parser = TestCaseFactory.GetSource(siteIn).CreateLinkParser();
        var htmlContent = File.ReadAllText(TestCaseFactory.GetArchiveParserTestCasePath(siteIn));
        var result = parser.GetLinksAsync("testBaseUrl", htmlContent).Result;
        Assert.That(result, Has.Count.EqualTo(expectedCount));
    }
}
