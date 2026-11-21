using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using System.Diagnostics.Metrics;
using Agitprop.Core.Enums;
using Agitprop.Messaging;
using System.Diagnostics;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// A hosted service that reads RSS feeds and publishes scraping jobs.
/// </summary>
public class RssFeedReader : BackgroundService
{
    private readonly string[] _feeds;
    private readonly ILogger<RssFeedReader> _logger;
    private readonly IPgmqClient _pgmqClient;
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

    public RssFeedReader(
        IConfiguration configuration,
        ILogger<RssFeedReader> logger,
        IPgmqClient pgmqClient)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _logger = logger;
        _feeds = configuration.GetSection("Feeds").Get<string[]>() ?? throw new ArgumentException("Feeds are not defined");
        _pgmqClient = pgmqClient;
        _interval = TimeSpan.FromMinutes(configuration.GetValue<double>("IntervalMinutes", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No activity is kept open here: a span owned by the host lifetime would become the parent of
        // everything the service ever does. Each cycle opens its own bounded trace instead.
        await _pgmqClient.EnsureQueuesAsync(stoppingToken);
        _logger.LogInformation(
            "Starting RSS Feed Reader. Interval: {Interval} minutes, Feeds: {FeedCount}",
            _interval.TotalMinutes,
            _feeds.Length);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                await RunCycleAsync();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    private async Task RunCycleAsync()
    {
        using var activity = _activitySource.StartActivity("rss.cycle", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";

        var publishAttempted = false;

        try
        {
            var (jobs, failedFeeds) = FetchScrapingJobs();

            if (failedFeeds > 0)
            {
                // One broken feed is not a failed cycle, but reporting success would hide it.
                outcome = "partial_failure";
            }

            if (jobs.Count == 0)
            {
                _logger.LogInformation("No new jobs fetched in this cycle");
            }
            else
            {
                _logger.LogDebug("Publishing {JobCount} scraping jobs", jobs.Count);
                publishAttempted = true;
                await _pgmqClient.SendBatchAsync(PgmqQueueNames.NewsfeedJobs, jobs);
                _jobsPublished.Add(jobs.Count);
            }

            _logger.LogInformation("RSS feed cycle completed; feeds: {FeedCount}, jobs published: {JobCount}",
                _feeds.Length, jobs.Count);

            activity?.SetTag("rss.feed.count", _feeds.Length);
            activity?.SetTag("rss.feeds.failed", failedFeeds);
            activity?.SetTag("rss.jobs.count", jobs.Count);
            activity?.SetTag("outcome", outcome);
            activity?.SetStatus(
                outcome == "success" ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                outcome);
        }
        catch (Exception ex)
        {
            outcome = "failure";
            if (publishAttempted)
            {
                _publishFailures.Add(1);
            }
            _logger.LogError(ex, "RSS feed cycle failed");
            activity?.SetTag("outcome", outcome);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
        }
        finally
        {
            _cycleDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private (List<NewsfeedJobDescrpition> Jobs, int FailedFeeds) FetchScrapingJobs()
    {
        using var activity = _activitySource.StartActivity("rss.fetch", ActivityKind.Internal);
        var scrapingJobs = new List<NewsfeedJobDescrpition>();
        var failedFeeds = 0;

        foreach (var feedUrl in _feeds)
        {
            using var feedActivity = _activitySource.StartActivity("rss.feed", ActivityKind.Internal);
            var safeFeedUrl = Core.TelemetryUrl.RedactQueryAndFragment(feedUrl);
            var feedHost = GetFeedHost(feedUrl);
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

                        return new NewsfeedJobDescrpition
                        {
                            Url = link ?? throw new ArgumentException("No link found"),
                            Type = PageContentType.Article
                        };
                    }).ToList();

                    feedActivity?.SetTag("feed.items.count", news.Count);
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
                failedFeeds++;
                _feedFailures.Add(1, new KeyValuePair<string, object?>("feed.host", feedHost));
                _logger.LogError(ex, "Error processing feed {FeedUrl}", safeFeedUrl);
                feedActivity?.SetTag("outcome", "failure");
                feedActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            }
        }

        _logger.LogDebug("Total scraping jobs fetched: {JobCount}", scrapingJobs.Count);
        activity?.SetTag("rss.feeds.failed", failedFeeds);
        activity?.SetTag("rss.jobs.count", scrapingJobs.Count);
        activity?.SetStatus(ActivityStatusCode.Ok, "FetchScrapingJobs completed");
        return (scrapingJobs, failedFeeds);
    }

    private static string GetFeedHost(string feedUrl) =>
        Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ? uri.Host : "unknown";
}
