using System.Net;
using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.PageRequester;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
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
        var spider = CreateSpider();
        var sink = new ArticleOutputSink(request.ShortenOutput);

        var job = new NewsfeedJobDescrpition
        {
            Type = PageContentType.Article,
            Url = request.Url
        };

        await spider.CrawlAsync(job.ConvertToScrapingJob(), sink, cancellationToken);
        return new ArticleScrapeExecutionResult(sink.OutputLines);
    }

    public async Task<ArchiveSiteScrapeExecutionResult> ExecuteArchiveSiteAsync(ArchiveSiteScrapeRequest request, CancellationToken cancellationToken = default)
    {
        var spider = CreateSpider();
        var sink = new ArchiveSink();
        var job = CreateArchiveJob(request.Date, request.Site);

        var jobs = await spider.CrawlAsync(job.ConvertToScrapingJob(), sink, cancellationToken);
        return new ArchiveSiteScrapeExecutionResult(job.Url, jobs);
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

    private static Spider CreateSpider()
    {
        var cookiesStorage = new CookieStorage();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        return new Spider(
            new PageTransport(
                new HttpStaticPageLoader(new RespectfulPageRequester(new CookieContainer()), cookiesStorage),
                new PuppeteerPageLoader(cookiesStorage),
                NullLogger<PageTransport>.Instance),
            configuration);
    }

    private static NewsfeedJobDescrpition CreateArchiveJob(DateOnly date, NewsSites site)
    {
        var url = site switch
        {
            NewsSites.Origo => $"https://www.origo.hu/hirarchivum/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.Ripost => $"https://ripost.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.Mandiner => $"https://mandiner.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.Metropol => $"https://metropol.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.MagyarNemzet => $"https://magyarnemzet.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.PestiSracok => $"https://www.pestisracok.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.MagyarJelen => $"https://www.magyarjelen.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.HuszonnegyHu => $"https://www.24.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.NegyNegyNegy => $"https://www.444.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.HVG => $"https://www.hvg.hu/frisshirek/{date.Year:D4}.{date.Month:D2}.{date.Day:D2}",
            NewsSites.Telex => $"https://telex.hu/sitemap/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}/news.xml",
            NewsSites.Index => $"https://index.hu/sitemap/cikkek_{date:yyyyMM}.xml",
            NewsSites.Merce => $"https://www.merce.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.Kurucinfo => throw new NotImplementedException("Kurucinfo scraping by date is not supported"),
            NewsSites.Alfahir => throw new NotImplementedException("Alfahir scraping by date is not supported"),
            NewsSites.RTL => throw new NotImplementedException("RTL scraping by date is not supported"),
            _ => throw new NotImplementedException($"Scraping by date is not supported for {site}"),
        };

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
