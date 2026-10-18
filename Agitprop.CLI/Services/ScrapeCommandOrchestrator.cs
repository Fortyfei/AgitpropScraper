using System.Net;
using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.PageRequester;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.CLI.Services;

public sealed class ScrapeCommandOrchestrator : IScrapeCommandOrchestrator
{
    private readonly IArticleJobPublisher _rabbitMqPublisher;
    private readonly IArticleJobPublisher _noOpPublisher;

    public ScrapeCommandOrchestrator()
        : this(new RabbitMqArticleJobPublisher(), new NoOpArticleJobPublisher())
    {
    }

    internal ScrapeCommandOrchestrator(IArticleJobPublisher rabbitMqPublisher, IArticleJobPublisher noOpPublisher)
    {
        _rabbitMqPublisher = rabbitMqPublisher;
        _noOpPublisher = noOpPublisher;
    }

    public async Task<ArticleScrapeExecutionResult> ExecuteArticleAsync(ArticleScrapeRequest request, CancellationToken cancellationToken = default)
    {
        var sink = new ArticleOutputSink(request.ShortenOutput);
        var pipeline = CreatePipeline(sink);

        var job = new NewsfeedJobDescrpition
        {
            Type = PageContentType.Article,
            Url = request.Url
        };

        await pipeline.CrawlAsync(job, cancellationToken: cancellationToken);
        return new ArticleScrapeExecutionResult(sink.OutputLines);
    }

    public async Task<ArchiveSiteScrapeExecutionResult> ExecuteArchiveSiteAsync(ArchiveSiteScrapeRequest request, CancellationToken cancellationToken = default)
    {
        var pipeline = CreatePipeline(new ArchiveSink());
        var job = CreateArchiveJob(pipeline.Sources, request.Date, request.Site);

        var jobs = await pipeline.CrawlAsync(job, cancellationToken: cancellationToken);
        return new ArchiveSiteScrapeExecutionResult(job.Url, jobs.Cast<ScrapingJobDescription>().ToList());
    }

    public async Task<PublishExecutionResult> PublishArticlesAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default)
    {
        var publisher = request.IsPublishingEnabled ? _rabbitMqPublisher : _noOpPublisher;
        return await publisher.PublishAsync(request, cancellationToken);
    }

    public async Task<RetryFailedFeedsExecutionResult> RetryFailedFeedsAsync(RetryFailedFeedsRequest request, CancellationToken cancellationToken = default)
    {
        var publisher = request.IsRetryEnabled ? _rabbitMqPublisher : _noOpPublisher;
        return await publisher.RetryFailedFeedsAsync(request, cancellationToken);
    }

    private static NewsfeedPipeline CreatePipeline(ISink sink)
    {
        var cookiesStorage = new CookieStorage();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var spider = new Spider(
            new PageTransport(
                new HttpStaticPageLoader(new RespectfulPageRequester(new CookieContainer()), cookiesStorage),
                new PuppeteerPageLoader(cookiesStorage),
                NullLogger<PageTransport>.Instance),
            configuration);

        return new NewsfeedPipeline(new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn()), spider, sink);
    }

    private static NewsfeedJobDescrpition CreateArchiveJob(INewsfeedSourceCatalog catalog, DateOnly date, SourceId site)
    {
        if (!catalog.TryGetById(site.Value, out var source))
            throw new NotImplementedException($"Unknown news source: {site}");

        var url = source.GetArchiveUrl(date)
            ?? throw new NotImplementedException($"{site} scraping by date is not supported");

        return new NewsfeedJobDescrpition
        {
            Url = url,
            Type = PageContentType.Archive
        };
    }

    private sealed class ArticleOutputSink : ISink
    {
        private readonly bool _shortenOutput;

        public ArticleOutputSink(bool shortenOutput)
        {
            _shortenOutput = shortenOutput;
        }

        public List<string> OutputLines { get; } = [];

        public Task<bool> CheckPageAlreadyVisited(string url)
        {
            return Task.FromResult(false);
        }

        public Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
        {
            foreach (var result in data)
            {
                OutputLines.Add($"Source: {url}");
                OutputLines.Add($"SourceSite: {result.SourceSite}");
                OutputLines.Add($"PublishDate: {result.PublishDate}");

                var text = _shortenOutput && result.Text.Length > 100
                    ? $"{result.Text[..50]}...{result.Text[^50..]}"
                    : result.Text;

                OutputLines.Add($"Text: {text}");
                OutputLines.Add(string.Empty);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ArchiveSink : ISink
    {
        public Task<bool> CheckPageAlreadyVisited(string url)
        {
            return Task.FromResult(false);
        }

        public Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
