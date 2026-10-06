using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using System.Diagnostics.Metrics;
using Agitprop.Core.Enums;
using MassTransit;
using System.Diagnostics;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// A hosted service that reads RSS feeds and publishes scraping jobs.
/// </summary>
public class RssFeedReader : IHostedService, IDisposable
{
    private readonly string[] _feeds;
    private readonly ILogger<RssFeedReader> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private static readonly ActivitySource _activitySource = new("Agitprop.RssFeedReader");
    private static readonly Meter _meter = new("Agitprop.RssFeedReader");
    private static readonly Counter<long> _itemsFetched = _meter.CreateCounter<long>(
        "rss.items.fetched",
        description: "Total items fetched from RSS feeds");
    private static readonly Counter<long> _jobsPublished = _meter.CreateCounter<long>(
        "rss.jobs.published",
        description: "Total scraping jobs published from RSS feeds");
    private static readonly Counter<long> _feedFailures = _meter.CreateCounter<long>(
        "rss.feed.failures",
        description: "Total failed RSS feed processing operations");
    private static readonly Counter<long> _publishFailures = _meter.CreateCounter<long>(
        "rss.publish.failures",
        description: "Total failures publishing scraping jobs");
    private static readonly Histogram<double> _cycleDuration = _meter.CreateHistogram<double>(
        "rss.cycle.duration",
        "ms",
        "Time spent fetching and publishing RSS scraping jobs");

    public RssFeedReader(IConfiguration configuration, ILogger<RssFeedReader> logger, IServiceScopeFactory scopeFactory)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _logger = logger;
        _feeds = configuration.GetSection("Feeds").Get<string[]>() ?? throw new ArgumentException("Feeds are not defined");
        _scopeFactory = scopeFactory;
        _interval = TimeSpan.FromMinutes(configuration.GetValue<double>("IntervalMinutes", 60));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("StartAsync");
        _logger.LogInformation("Starting RSS Feed Reader. Interval: {Interval} minutes, Feeds: {FeedCount}", _interval.TotalMinutes, _feeds.Length);
        var timer = new Timer(async _ => await ExecuteTask(_), null,TimeSpan.Zero, _interval);
        return;
    }

    private async Task ExecuteTask(object? state)
    {
        using var activity = _activitySource.StartActivity("ExecuteTask", ActivityKind.Producer);
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";

        var publishAttempted = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var jobs = FetchScrapingJobs();
            if (jobs.Count == 0)
            {
                _logger.LogInformation("No new jobs fetched in this cycle");
            }
            else
            {
                _logger.LogDebug("Publishing {JobCount} scraping jobs", jobs.Count);
                publishAttempted = true;
                await publishEndpoint.PublishBatch(jobs);
                _jobsPublished.Add(jobs.Count);
            }

            _logger.LogInformation("RSS feed cycle completed; feeds: {FeedCount}, jobs published: {JobCount}",
                _feeds.Length, jobs.Count);
            activity?.SetTag("rss.jobs.count", jobs.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            outcome = "failure";
            if (publishAttempted)
            {
                _publishFailures.Add(1);
            }
            _logger.LogError(ex, "RSS feed cycle failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            _cycleDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("StopAsync");
        _logger.LogInformation("Stopping RSS Feed Reader");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing RSS Feed Reader resources");
    }
    
    private List<NewsfeedJobDescrpition> FetchScrapingJobs()
    {
        using var activity = _activitySource.StartActivity("FetchScrapingJobs", ActivityKind.Producer);
        var scrapingJobs = new List<NewsfeedJobDescrpition>();

        foreach (var feedUrl in _feeds)
        {
            using var feedActivity = _activitySource.StartActivity("ProcessFeed", ActivityKind.Consumer);
            var safeFeedUrl = Core.TelemetryUrl.RedactQueryAndFragment(feedUrl);
            var feedHost = GetFeedHost(feedUrl);
            feedActivity?.SetTag("feed.url", safeFeedUrl);
            feedActivity?.SetTag("feed.host", feedHost);
            _logger.LogDebug("Reading RSS feed: {FeedUrl}", safeFeedUrl);

            try
            {
                using var reader = XmlReader.Create(feedUrl);
                var feed = SyndicationFeed.Load(reader);

                if (feed != null)
                {
                    var news = feed.Items.Select(item =>
                    {
                        var link = item.Links.FirstOrDefault()?.Uri?.GetLeftPart(UriPartial.Path);
                        feedActivity?.AddEvent(new ActivityEvent("FeedItemRead", default, new ActivityTagsCollection
                        {
                            { "item.title", item.Title.Text },
                            { "item.link", link is null ? "null" : Core.TelemetryUrl.RedactQueryAndFragment(link) }
                        }));

                        return new NewsfeedJobDescrpition
                        {
                            Url = link ?? throw new ArgumentException("No link found"),
                            Type = PageContentType.Article
                        };
                    }).ToList();

                    _itemsFetched.Add(news.Count, new KeyValuePair<string, object?>("feed.host", feedHost));
                    _logger.LogDebug("Fetched {ItemCount} items from feed {FeedUrl}", news.Count, safeFeedUrl);
                    scrapingJobs.AddRange(news);
                }
                else
                {
                    _logger.LogWarning("RSS feed {FeedUrl} returned no items", safeFeedUrl);
                }

                feedActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                _feedFailures.Add(1, new KeyValuePair<string, object?>("feed.host", feedHost));
                _logger.LogError(ex, "Error processing feed {FeedUrl}", safeFeedUrl);
                feedActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }

        _logger.LogDebug("Total scraping jobs fetched: {JobCount}", scrapingJobs.Count);
        activity?.SetStatus(ActivityStatusCode.Ok, "FetchScrapingJobs completed");
        return scrapingJobs;
    }

    private static string GetFeedHost(string feedUrl) =>
        Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ? uri.Host : "unknown";
}
