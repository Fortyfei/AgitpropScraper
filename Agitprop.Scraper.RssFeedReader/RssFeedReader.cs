using System.Diagnostics;
using System.Diagnostics.Metrics;

using Agitprop.Core.Enums;
using Agitprop.Messaging;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// A hosted service that periodically reads RSS feeds and publishes the articles found in them as
/// scraping jobs. Feeds are read with conditional requests and only links that were not published
/// before are queued; the watermark advances only after a successful publish, so a failed cycle is
/// retried instead of silently losing articles.
/// </summary>
public sealed class RssFeedReader : BackgroundService
{
    public const string ActivitySourceName = "Agitprop.RssFeedReader";

    private const string OutcomeSuccess = "success";
    private const string OutcomePartialFailure = "partial_failure";
    private const string OutcomeFailure = "failure";
    private const string OutcomeNotModified = "not_modified";
    private const string OutcomeCancelled = "cancelled";

    private static readonly ActivitySource _activitySource = new(ActivitySourceName);
    private static readonly Meter _meter = new(ActivitySourceName);

    private static readonly Counter<long> _itemsFetched = _meter.CreateCounter<long>(
        "rss.items.fetched",
        description: "Items read from RSS feeds");

    private static readonly Counter<long> _itemsDuplicate = _meter.CreateCounter<long>(
        "rss.items.duplicate",
        description: "Items skipped because their link was already published");

    private static readonly Counter<long> _jobsPublished = _meter.CreateCounter<long>(
        "rss.jobs.published",
        description: "Scraping jobs published from RSS feeds");

    private static readonly Counter<long> _feedFailures = _meter.CreateCounter<long>(
        "rss.feed.failures",
        description: "RSS feeds that could not be read, grouped by host and failure reason");

    private static readonly Counter<long> _publishFailures = _meter.CreateCounter<long>(
        "rss.publish.failures",
        description: "Failures publishing the batch of RSS scraping jobs");

    private static readonly Histogram<double> _cycleDuration = _meter.CreateHistogram<double>(
        "rss.cycle.duration",
        "ms",
        "Time spent reading RSS feeds and publishing their articles");

    private static readonly Histogram<double> _feedDuration = _meter.CreateHistogram<double>(
        "rss.feed.duration",
        "ms",
        "Time spent reading and parsing one RSS feed");

    private readonly RssFeedReaderOptions _options;
    private readonly IRssFeedClient _feedClient;
    private readonly IPgmqClient _pgmqClient;
    private readonly RssFeedState _state;
    private readonly ILogger<RssFeedReader> _logger;
    private readonly TimeProvider _timeProvider;

    public RssFeedReader(
        RssFeedReaderOptions options,
        IRssFeedClient feedClient,
        IPgmqClient pgmqClient,
        RssFeedState state,
        ILogger<RssFeedReader> logger,
        TimeProvider timeProvider)
    {
        _options = options;
        _feedClient = feedClient;
        _pgmqClient = pgmqClient;
        _state = state;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No activity is kept open here: a span owned by the host lifetime would become the parent of
        // everything the service ever does. Each cycle opens its own bounded trace instead.
        await _pgmqClient.EnsureQueuesAsync(stoppingToken);
        _logger.LogInformation(
            "Starting RSS Feed Reader. Interval: {IntervalMinutes} minutes, feeds: {FeedCount}, " +
            "max concurrency: {MaxConcurrentFeeds}, request timeout: {RequestTimeoutSeconds} s",
            _options.Interval.TotalMinutes,
            _options.Feeds.Count,
            _options.MaxConcurrentFeeds,
            _options.RequestTimeout.TotalSeconds);

        using var timer = new PeriodicTimer(_options.Interval, _timeProvider);
        try
        {
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    internal async Task<RssCycleSummary> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity(BoundedTraceSampler.RssCycleSpanName, ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();
        var summary = new RssCycleSummary { FeedsConfigured = _options.Feeds.Count };
        var outcome = OutcomeSuccess;

        try
        {
            var fetches = await FetchFeedsAsync(cancellationToken);
            var pending = SelectPendingLinks(fetches, summary);

            if (pending.Count > 0)
            {
                await PublishAsync(pending, cancellationToken);
                summary.ItemsPublished = pending.Count;
                _jobsPublished.Add(pending.Count);

                foreach (var pendingLink in pending)
                {
                    pendingLink.Cursor.MarkPublished(pendingLink.Link);
                }
            }

            // Nothing is pending at this point, so the validators are safe to advance. A cycle that
            // threw before this line leaves both the validators and the watermark untouched.
            foreach (var fetch in fetches)
            {
                if (fetch.Result.Outcome == RssFeedOutcome.Success)
                {
                    fetch.Cursor.UpdateValidators(fetch.Result.EntityTag, fetch.Result.LastModified);
                }
            }

            outcome = summary.FeedsFailed switch
            {
                0 => OutcomeSuccess,
                var failed when failed < summary.FeedsConfigured => OutcomePartialFailure,
                _ => OutcomeFailure
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = OutcomeCancelled;
            throw;
        }
        catch (Exception exception)
        {
            outcome = OutcomeFailure;
            _logger.LogError(exception, "RSS feed cycle failed");
        }
        finally
        {
            summary.Outcome = outcome;
            summary.Duration = stopwatch.Elapsed;
            ReportCycle(activity, summary);
        }

        return summary;
    }

    private async Task<IReadOnlyList<FeedFetch>> FetchFeedsAsync(CancellationToken cancellationToken)
    {
        // The semaphore is intentionally not disposed: on cancellation the remaining fetches finish
        // after the first exception surfaces, and releasing a disposed semaphore would fault them.
        var gate = new SemaphoreSlim(_options.MaxConcurrentFeeds);

        var fetches = _options.Feeds.Select(async feedUrl =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await FetchFeedAsync(feedUrl, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(fetches);
    }

    private async Task<FeedFetch> FetchFeedAsync(string feedUrl, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("rss.feed", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();
        var safeFeedUrl = Core.TelemetryUrl.RedactQueryAndFragment(feedUrl);
        var feedHost = Core.TelemetryUrl.GetServerAddress(feedUrl);
        var cursor = _state.Get(feedUrl);
        var outcome = OutcomeSuccess;

        activity?.SetTag("feed.host", feedHost);
        _logger.LogDebug("Reading RSS feed: {FeedUrl}", safeFeedUrl);

        try
        {
            RssFeedFetchResult result;
            try
            {
                result = await _feedClient.FetchAsync(feedUrl, cursor.EntityTag, cursor.LastModified, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                result = new RssFeedFetchResult { Outcome = RssFeedOutcome.Failed, FailureReason = "unknown" };
            }

            activity?.SetTag("http.response.status_code", result.StatusCode);

            switch (result.Outcome)
            {
                case RssFeedOutcome.NotModified:
                    outcome = OutcomeNotModified;
                    activity?.SetTag("feed.not_modified", true);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogDebug("RSS feed not modified since the last cycle: {FeedUrl}", safeFeedUrl);
                    break;

                case RssFeedOutcome.Failed:
                    outcome = OutcomeFailure;
                    activity?.SetTag("failure.reason", result.FailureReason);
                    activity?.SetStatus(ActivityStatusCode.Error, result.FailureReason);
                    _feedFailures.Add(1,
                        new KeyValuePair<string, object?>("feed.host", feedHost),
                        new KeyValuePair<string, object?>("reason", result.FailureReason ?? "unknown"));
                    _logger.LogWarning(
                        "RSS feed {FeedUrl} failed: {FailureReason} (status {StatusCode})",
                        safeFeedUrl,
                        result.FailureReason,
                        result.StatusCode);
                    break;

                default:
                    activity?.SetTag("feed.items.count", result.ItemsParsed);
                    activity?.SetTag("feed.items.invalid", result.ItemsInvalid);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _itemsFetched.Add(result.ItemsParsed, new KeyValuePair<string, object?>("feed.host", feedHost));

                    if (result.Truncated)
                    {
                        activity?.SetTag("feed.truncated", true);
                        _logger.LogWarning(
                            "RSS feed {FeedUrl} returned more items than the {MaxItemsPerFeed} item limit; the rest is ignored",
                            safeFeedUrl,
                            _options.MaxItemsPerFeed);
                    }

                    _logger.LogDebug(
                        "Fetched {ItemCount} items ({InvalidCount} without a usable link) from feed {FeedUrl}",
                        result.ItemsParsed,
                        result.ItemsInvalid,
                        safeFeedUrl);
                    break;
            }

            return new FeedFetch(feedUrl, cursor, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = OutcomeCancelled;
            throw;
        }
        finally
        {
            _feedDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("feed.host", feedHost),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private List<PendingLink> SelectPendingLinks(IReadOnlyList<FeedFetch> fetches, RssCycleSummary summary)
    {
        var pending = new List<PendingLink>();
        var cycleLinks = new HashSet<ulong>();

        foreach (var fetch in fetches)
        {
            switch (fetch.Result.Outcome)
            {
                case RssFeedOutcome.NotModified:
                    summary.FeedsNotModified++;
                    break;

                case RssFeedOutcome.Failed:
                    summary.FeedsFailed++;
                    break;

                default:
                    summary.ItemsParsed += fetch.Result.ItemsParsed;
                    summary.ItemsInvalid += fetch.Result.ItemsInvalid;

                    foreach (var link in fetch.Result.Links)
                    {
                        if (fetch.Cursor.HasPublished(link) || !cycleLinks.Add(RssFeedState.HashLink(link)))
                        {
                            summary.ItemsDuplicate++;
                            _itemsDuplicate.Add(1,
                                new KeyValuePair<string, object?>("feed.host",
                                    Core.TelemetryUrl.GetServerAddress(fetch.FeedUrl)));
                            continue;
                        }

                        pending.Add(new PendingLink(link, fetch.Cursor));
                    }

                    break;
            }
        }

        return pending;
    }

    private async Task PublishAsync(List<PendingLink> pending, CancellationToken cancellationToken)
    {
        var jobs = pending
            .Select(pendingLink => new NewsfeedJobDescrpition
            {
                Url = pendingLink.Link,
                Type = PageContentType.Article
            })
            .ToList();

        using var activity = _activitySource.StartActivity("rss.publish", ActivityKind.Internal);
        activity?.SetTag("messaging.destination.name", PgmqQueueNames.NewsfeedJobs);
        activity?.SetTag("messaging.batch.size", jobs.Count);

        try
        {
            await _pgmqClient.SendBatchAsync(PgmqQueueNames.NewsfeedJobs, jobs, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Published {JobCount} scraping jobs from RSS feeds", jobs.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("outcome", OutcomeCancelled);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("outcome", OutcomeFailure);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            _publishFailures.Add(1);
            throw;
        }
    }

    private void ReportCycle(Activity? activity, RssCycleSummary summary)
    {
        _cycleDuration.Record(summary.Duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", summary.Outcome));

        activity?.SetTag("rss.feeds.configured", summary.FeedsConfigured);
        activity?.SetTag("rss.feeds.not_modified", summary.FeedsNotModified);
        activity?.SetTag("rss.feeds.failed", summary.FeedsFailed);
        activity?.SetTag("rss.items.parsed", summary.ItemsParsed);
        activity?.SetTag("rss.items.invalid", summary.ItemsInvalid);
        activity?.SetTag("rss.items.duplicate", summary.ItemsDuplicate);
        activity?.SetTag("rss.items.published", summary.ItemsPublished);
        activity?.SetTag("outcome", summary.Outcome);

        if (summary.Outcome == OutcomeCancelled)
        {
            // Shutdown is expected, not a failure to report.
            return;
        }

        activity?.SetStatus(
            summary.Outcome == OutcomeSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
            summary.Outcome);

        _logger.LogInformation(
            "RSS cycle finished in {DurationMilliseconds} ms: feeds {FeedCount} ({NotModifiedCount} not modified, " +
            "{FailedCount} failed), items {ItemCount} ({InvalidCount} invalid, {DuplicateCount} duplicate, " +
            "{PublishedCount} published), outcome {Outcome}",
            Math.Round(summary.Duration.TotalMilliseconds),
            summary.FeedsConfigured,
            summary.FeedsNotModified,
            summary.FeedsFailed,
            summary.ItemsParsed,
            summary.ItemsInvalid,
            summary.ItemsDuplicate,
            summary.ItemsPublished,
            summary.Outcome);
    }

    private sealed record FeedFetch(string FeedUrl, FeedCursor Cursor, RssFeedFetchResult Result);

    private readonly record struct PendingLink(string Link, FeedCursor Cursor);
}

internal sealed record RssCycleSummary
{
    public required int FeedsConfigured { get; init; }

    public int FeedsNotModified { get; set; }

    public int FeedsFailed { get; set; }

    public int ItemsParsed { get; set; }

    public int ItemsInvalid { get; set; }

    public int ItemsDuplicate { get; set; }

    public int ItemsPublished { get; set; }

    public string Outcome { get; set; } = "success";

    public TimeSpan Duration { get; set; }
}
