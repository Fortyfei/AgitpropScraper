using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Sources;

public sealed class NewsfeedPipeline : INewsfeedPipeline
{
    private readonly ISpider _spider;
    private readonly ISink _defaultSink;

    public NewsfeedPipeline(INewsfeedSourceCatalog sources, ISpider spider, ISink defaultSink)
    {
        Sources = sources;
        _spider = spider;
        _defaultSink = defaultSink;
    }

    public INewsfeedSourceCatalog Sources { get; }

    public async Task<IReadOnlyList<NewsfeedJobDescrpition>> CrawlAsync(
        NewsfeedJobDescrpition job,
        ISink? sinkOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var host = new Uri(job.Url).Host;
        if (!Sources.TryGetByHost(host, out var source))
            throw new ArgumentException($"Not supported news source: {host}", nameof(job));

        var scrapingJob = job.Type switch
        {
            PageContentType.Article => CreateArticleJob(source, job.Url),
            PageContentType.Archive => CreateArchiveJob(source, job.Url),
            _ => throw new ArgumentException($"Not supported newsfeed job type: {job.Type}", nameof(job))
        };

        var discovered = await _spider.CrawlAsync(scrapingJob, sinkOverride ?? _defaultSink, cancellationToken);
        return discovered.Cast<NewsfeedJobDescrpition>().ToList();
    }

    private static ScrapingJob CreateArticleJob(INewsfeedSource source, string url) => new()
    {
        Url = url,
        PageCategory = PageCategory.TargetPage,
        PageType = PageType.Static,
        LinkParsers = [source.CreateLinkParser()],
        ContentParsers = [source.CreateContentParser()]
    };

    private static ScrapingJob CreateArchiveJob(INewsfeedSource source, string url) => new()
    {
        Url = url,
        PageCategory = PageCategory.PageWithPagination,
        PageType = source.ArchivePageType,
        Actions = source.CreateArchiveActions(),
        LinkParsers = [source.CreateLinkParser()],
        ContentParsers = [source.CreateContentParser()],
        Pagination = source.CreatePaginator()
    };
}
