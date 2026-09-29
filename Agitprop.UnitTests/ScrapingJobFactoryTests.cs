using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sinks.Newsfeed.Factories;

namespace Agitprop.UnitTests;

public class ScrapingJobFactoryTests
{
    [Test]
    public void GetArticleScrapingJob_CreatesStaticTargetJobWithParsers()
    {
        const string url = "https://telex.hu/example";

        var job = ScrapingJobFactory.GetArticleScrapingJob(NewsSites.Telex, url);

        Assert.Multiple(() =>
        {
            Assert.That(job.Url, Is.EqualTo(url));
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.TargetPage));
            Assert.That(job.PageType, Is.EqualTo(PageType.Static));
            Assert.That(job.ContentParsers.Count(), Is.EqualTo(1));
            Assert.That(job.LinkParsers.Count(), Is.EqualTo(1));
            Assert.That(job.Pagination, Is.Null);
        });
    }

    [TestCase(NewsSites.Telex, PageType.Static)]
    [TestCase(NewsSites.NegyNegyNegy, PageType.Dynamic)]
    public void GetArchiveScrapingJob_ConfiguresPageTypeAndPagination(
        NewsSites site,
        PageType expectedPageType)
    {
        var job = ScrapingJobFactory.GetArchiveScrapingJob(site, "https://example.test/archive");

        Assert.Multiple(() =>
        {
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.PageWithPagination));
            Assert.That(job.PageType, Is.EqualTo(expectedPageType));
            Assert.That(job.ContentParsers.Count(), Is.EqualTo(1));
            Assert.That(job.LinkParsers.Count(), Is.EqualTo(1));
            Assert.That(job.Pagination, Is.Not.Null);
        });

        if (site == NewsSites.NegyNegyNegy)
        {
            Assert.That(job.Actions, Has.Count.EqualTo(1));
            Assert.That(job.Actions![0].Type, Is.EqualTo(PageActionType.Execute));
        }
        else
        {
            Assert.That(job.Actions, Is.Null);
        }
    }

    [TestCase("https://origo.hu/article")]
    [TestCase("https://www.origo.hu/article")]
    public void ConvertToScrapingJob_MapsArticleUrlToTargetPage(string url)
    {
        var jobDescription = new NewsfeedJobDescrpition
        {
            Url = url,
            Type = PageContentType.Article
        };

        var job = jobDescription.ConvertToScrapingJob();

        Assert.Multiple(() =>
        {
            Assert.That(job.Url, Is.EqualTo(url));
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.TargetPage));
            Assert.That(job.PageType, Is.EqualTo(PageType.Static));
        });
    }

    [Test]
    public void ConvertToScrapingJob_MapsArchiveUrlToPaginatedPage()
    {
        var jobDescription = new NewsfeedJobDescrpition
        {
            Url = "https://444.hu/archive",
            Type = PageContentType.Archive
        };

        var job = jobDescription.ConvertToScrapingJob();

        Assert.Multiple(() =>
        {
            Assert.That(job.PageCategory, Is.EqualTo(PageCategory.PageWithPagination));
            Assert.That(job.PageType, Is.EqualTo(PageType.Dynamic));
            Assert.That(job.Actions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ConvertToScrapingJob_WhenHostIsUnsupported_ThrowsArgumentException()
    {
        var jobDescription = new NewsfeedJobDescrpition
        {
            Url = "https://unknown.test/article",
            Type = PageContentType.Article
        };

        Assert.Throws<ArgumentException>(() => jobDescription.ConvertToScrapingJob());
    }
}