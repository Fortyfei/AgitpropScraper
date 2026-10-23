using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Scraper.Consumer.Consumers;
using Agitprop.Sources;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.UnitTests;

public class NewsfeedJobEligibilityTests
{
    private readonly INewsfeedSourceCatalog _sources = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

    [TestCase("https://www.hvg.hu/article", PageContentType.Article, true)]
    [TestCase("https://img.hvg.hu/image.jpg", PageContentType.Article, false)]
    [TestCase("https://www.hvg.hu/image.webp?width=800", PageContentType.Article, false)]
    [TestCase("https://www.hvg.hu/archive.xml", PageContentType.Archive, true)]
    [TestCase("https://unknown.test/article", PageContentType.Article, false)]
    [TestCase("file:///article.html", PageContentType.Article, false)]
    [TestCase("not a url", PageContentType.Article, false)]
    public void ShouldProcess_RejectsUnsupportedAndAssetJobs(
        string url,
        PageContentType type,
        bool expected)
    {
        var job = new NewsfeedJobDescrpition { Url = url, Type = type };

        Assert.That(NewsfeedJobEligibility.ShouldProcess(job, _sources), Is.EqualTo(expected));
    }
}
