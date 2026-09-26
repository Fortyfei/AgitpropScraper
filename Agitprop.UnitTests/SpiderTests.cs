using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;

namespace Agitprop.UnitTests;

public class SpiderTests
{
    private const string PageUrl = "https://example.test/article";

    [Test]
    public async Task CrawlAsync_WhenPageWasVisited_DoesNotLoadOrEmit()
    {
        var transport = new RecordingPageTransport();
        var sink = new RecordingSink(alreadyVisited: true);
        var spider = CreateSpider(transport);

        var result = await spider.CrawlAsync(CreateJob(), sink);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Empty);
            Assert.That(transport.CallCount, Is.Zero);
            Assert.That(sink.EmitCallCount, Is.Zero);
        });
    }

    [Test]
    public async Task CrawlAsync_WhenTargetPageIsParsed_EmitsResultAndPassesLoadOptions()
    {
        var transport = new RecordingPageTransport();
        var sink = new RecordingSink();
        var parsedResult = CreateContentResult("article text");
        var parser = new RecordingContentParser(_ => parsedResult);
        var job = CreateJob(
            category: PageCategory.TargetPage,
            pageType: PageType.Dynamic,
            contentParsers: [parser],
            actions: [new PageAction(PageActionType.Click)]);
        var spider = CreateSpider(transport);

        var result = await spider.CrawlAsync(job, sink);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Empty);
            Assert.That(parser.CallCount, Is.EqualTo(1));
            Assert.That(sink.EmitCallCount, Is.EqualTo(1));
            Assert.That(sink.EmittedUrl, Is.EqualTo(PageUrl));
            Assert.That(sink.EmittedData, Is.EqualTo(new[] { parsedResult }));
            Assert.That(transport.LastOptions?.RequiresJavaScript, Is.True);
            Assert.That(transport.LastOptions?.Actions, Is.SameAs(job.Actions));
        });
    }

    [Test]
    public void CrawlAsync_WhenTargetParserFails_ThrowsAndDoesNotEmit()
    {
        var transport = new RecordingPageTransport();
        var sink = new RecordingSink();
        var parser = new RecordingContentParser(_ => throw new FormatException("invalid article"));
        var spider = CreateSpider(transport);

        var exception = Assert.ThrowsAsync<Agitprop.Core.Exceptions.ContentParserException>(
            () => spider.CrawlAsync(CreateJob(PageCategory.TargetPage, contentParsers: [parser]), sink));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.InnerException, Is.TypeOf<FormatException>());
            Assert.That(sink.EmitCallCount, Is.Zero);
        });
    }

    [Test]
    public void CrawlAsync_WhenTargetPageHasNoParsers_ThrowsAndDoesNotEmit()
    {
        var sink = new RecordingSink();
        var spider = CreateSpider(new RecordingPageTransport());

        Assert.ThrowsAsync<Agitprop.Core.Exceptions.ContentParserException>(
            () => spider.CrawlAsync(CreateJob(PageCategory.TargetPage), sink));

        Assert.That(sink.EmitCallCount, Is.Zero);
    }

    [Test]
    public async Task CrawlAsync_WhenTransitPageHasLinks_ReturnsDiscoveredJobs()
    {
        var expectedJob = new ScrapingJobDescription { Url = "https://example.test/next" };
        var linkParser = new RecordingLinkParser([expectedJob]);
        var spider = CreateSpider(new RecordingPageTransport());

        var result = await spider.CrawlAsync(
            CreateJob(linkParsers: [linkParser]), new RecordingSink());

        Assert.That(result, Is.EqualTo(new[] { expectedJob }));
        Assert.That(linkParser.CallCount, Is.EqualTo(1));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task CrawlAsync_OnlyAddsNextPageWhenContinuousPaginationIsEnabled(
        bool continuousEnabled,
        int expectedCallCount)
    {
        var nextPage = new ScrapingJobDescription { Url = "https://example.test/archive?page=2" };
        var paginator = new RecordingPaginator(nextPage);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Continous"] = continuousEnabled.ToString()
            })
            .Build();
        var spider = CreateSpider(new RecordingPageTransport(), configuration);

        var result = await spider.CrawlAsync(
            CreateJob(PageCategory.PageWithPagination, paginator: paginator), new RecordingSink());

        Assert.That(result, Has.Count.EqualTo(expectedCallCount));
        Assert.That(paginator.CallCount, Is.EqualTo(expectedCallCount));
        if (continuousEnabled)
        {
            Assert.That(result[0], Is.SameAs(nextPage));
        }
    }

    [Test]
    public void CrawlAsync_WhenCanceled_DoesNotCheckOrLoadPage()
    {
        var transport = new RecordingPageTransport();
        var sink = new RecordingSink();
        var spider = CreateSpider(transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => spider.CrawlAsync(CreateJob(), sink, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(sink.CheckCallCount, Is.Zero);
            Assert.That(transport.CallCount, Is.Zero);
        });
    }

    private static Spider CreateSpider(
        RecordingPageTransport transport,
        IConfiguration? configuration = null) =>
        new(transport, configuration ?? new ConfigurationBuilder().Build());

    private static ScrapingJob CreateJob(
        PageCategory category = PageCategory.TransitPage,
        PageType pageType = PageType.Static,
        IEnumerable<IContentParser>? contentParsers = null,
        IEnumerable<ILinkParser>? linkParsers = null,
        IPaginator? paginator = null,
        List<PageAction>? actions = null) => new()
        {
            Url = PageUrl,
            PageCategory = category,
            PageType = pageType,
            Actions = actions,
            ContentParsers = contentParsers ?? [],
            LinkParsers = linkParsers ?? [],
            Pagination = paginator
        };

    private static ContentParserResult CreateContentResult(string text) => new()
    {
        SourceSite = NewsSites.Telex,
        PublishDate = new DateTime(2026, 1, 1),
        Text = text
    };

    private sealed class RecordingPageTransport : IPageTransport
    {
        public int CallCount { get; private set; }
        public PageLoadOptions? LastOptions { get; private set; }

        public Task<PageLoadResult> LoadAsync(string url, PageLoadOptions? options = null, CancellationToken ct = default)
        {
            CallCount++;
            LastOptions = options;
            return Task.FromResult(new PageLoadResult(
                "<html><body>test page</body></html>", new Uri(url), "test", TimeSpan.Zero));
        }
    }

    private sealed class RecordingSink(bool alreadyVisited = false) : ISink
    {
        public int CheckCallCount { get; private set; }
        public int EmitCallCount { get; private set; }
        public string? EmittedUrl { get; private set; }
        public List<ContentParserResult>? EmittedData { get; private set; }

        public Task<bool> CheckPageAlreadyVisited(string url)
        {
            CheckCallCount++;
            return Task.FromResult(alreadyVisited);
        }

        public Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
        {
            EmitCallCount++;
            EmittedUrl = url;
            EmittedData = data;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContentParser(Func<HtmlDocument, ContentParserResult> parse) : IContentParser
    {
        public int CallCount { get; private set; }

        public Task<ContentParserResult> ParseContentAsync(HtmlDocument html)
        {
            CallCount++;
            return Task.FromResult(parse(html));
        }

        public Task<ContentParserResult> ParseContentAsync(string html)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);
            return ParseContentAsync(document);
        }
    }

    private sealed class RecordingLinkParser(List<ScrapingJobDescription> links) : ILinkParser
    {
        public int CallCount { get; private set; }

        public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
        {
            CallCount++;
            return Task.FromResult(links);
        }

        public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc) =>
            GetLinksAsync(baseUrl, doc.ParsedText);
    }

    private sealed class RecordingPaginator(ScrapingJobDescription nextPage) : IPaginator
    {
        public int CallCount { get; private set; }

        public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
        {
            CallCount++;
            return Task.FromResult(nextPage);
        }
    }
}