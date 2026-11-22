using System.Diagnostics;

using Agitprop.Messaging;
using Agitprop.Scraper.RssFeedReader;
using Agitprop.Sinks.Newsfeed;

using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class RssFeedReaderTests
{
    private const string FeedA = "https://site-a.hu/feed.xml";
    private const string FeedB = "https://site-b.hu/feed.xml";

    [Test]
    public async Task RunCycle_PublishesOnlyLinksThatWereNotPublishedBefore()
    {
        var client = new FakeFeedClient();
        var queue = new RecordingQueue();
        var reader = CreateReader(client, queue, FeedA);

        client.Respond = _ => Success("https://site-a.hu/cikk/1", "https://site-a.hu/cikk/2");
        var first = await reader.RunCycleAsync(CancellationToken.None);

        client.Respond = _ => Success(
            "https://site-a.hu/cikk/1",
            "https://site-a.hu/cikk/2",
            "https://site-a.hu/cikk/3");
        var second = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.ItemsPublished, Is.EqualTo(2));
            Assert.That(second.ItemsPublished, Is.EqualTo(1));
            Assert.That(second.ItemsDuplicate, Is.EqualTo(2));
            Assert.That(queue.Batches, Has.Count.EqualTo(2));
            Assert.That(queue.Batches[0], Is.EqualTo(new[] { "https://site-a.hu/cikk/1", "https://site-a.hu/cikk/2" }));
            Assert.That(queue.Batches[1], Is.EqualTo(new[] { "https://site-a.hu/cikk/3" }));
        });
    }

    [Test]
    public async Task RunCycle_WhenPublishFails_RetriesTheSameLinksAndKeepsTheValidators()
    {
        var client = new FakeFeedClient
        {
            Respond = _ => SuccessWithValidator("etag-v1", "https://site-a.hu/cikk/1")
        };
        var queue = new RecordingQueue { SendException = new InvalidOperationException("queue down") };
        var reader = CreateReader(client, queue, FeedA);

        var failed = await reader.RunCycleAsync(CancellationToken.None);

        queue.SendException = null;
        var retried = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Outcome, Is.EqualTo("failure"));
            Assert.That(failed.ItemsPublished, Is.Zero);
            Assert.That(retried.Outcome, Is.EqualTo("success"));
            Assert.That(retried.ItemsPublished, Is.EqualTo(1));
            Assert.That(queue.Batches.Single(), Is.EqualTo(new[] { "https://site-a.hu/cikk/1" }));
            // A validator committed by the failed cycle would let the retry get a 304 for a link that
            // was never published, losing the article.
            Assert.That(client.RequestedEntityTags, Is.EqualTo(new string?[] { null, null }));
        });
    }

    [Test]
    public async Task RunCycle_WhenOneFeedFails_ReportsPartialFailureAndPublishesTheOtherFeed()
    {
        var client = new FakeFeedClient
        {
            Respond = feedUrl => feedUrl == FeedA
                ? Success("https://site-a.hu/cikk/1")
                : Failed()
        };
        var queue = new RecordingQueue();
        var reader = CreateReader(client, queue, FeedA, FeedB);

        var summary = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Outcome, Is.EqualTo("partial_failure"));
            Assert.That(summary.FeedsFailed, Is.EqualTo(1));
            Assert.That(queue.Batches.Single(), Is.EqualTo(new[] { "https://site-a.hu/cikk/1" }));
        });
    }

    [Test]
    public async Task RunCycle_WhenEveryFeedFails_ReportsFailure()
    {
        var client = new FakeFeedClient { Respond = _ => Failed() };
        var reader = CreateReader(client, new RecordingQueue(), FeedA, FeedB);

        var summary = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Outcome, Is.EqualTo("failure"));
            Assert.That(summary.FeedsFailed, Is.EqualTo(2));
            Assert.That(summary.ItemsPublished, Is.Zero);
        });
    }

    [Test]
    public async Task RunCycle_WhenNoFeedChanged_PublishesNothing()
    {
        var client = new FakeFeedClient
        {
            Respond = _ => new RssFeedFetchResult { Outcome = RssFeedOutcome.NotModified, StatusCode = 304 }
        };
        var queue = new RecordingQueue();
        var reader = CreateReader(client, queue, FeedA);

        var summary = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Outcome, Is.EqualTo("success"));
            Assert.That(summary.FeedsNotModified, Is.EqualTo(1));
            Assert.That(summary.ItemsDuplicate, Is.Zero);
            Assert.That(queue.Batches, Is.Empty);
        });
    }

    [Test]
    public async Task RunCycle_DeduplicatesTheSameLinkAcrossFeeds()
    {
        var client = new FakeFeedClient { Respond = _ => Success("https://site-a.hu/cikk/1") };
        var queue = new RecordingQueue();
        var reader = CreateReader(client, queue, FeedA, FeedB);

        var summary = await reader.RunCycleAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(summary.ItemsPublished, Is.EqualTo(1));
            Assert.That(summary.ItemsDuplicate, Is.EqualTo(1));
            Assert.That(queue.Batches.Single(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RunCycle_OpensCycleFeedAndPublishSpansWithoutPerItemSpans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RssFeedReader.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var client = new FakeFeedClient
        {
            Respond = feedUrl => feedUrl == FeedA
                ? Success("https://site-a.hu/cikk/1", "https://site-a.hu/cikk/2")
                : Success("https://site-b.hu/cikk/1", "https://site-b.hu/cikk/2")
        };
        var reader = CreateReader(client, new RecordingQueue(), FeedA, FeedB);

        var summary = await reader.RunCycleAsync(CancellationToken.None);

        var cycle = activities.Single(activity => activity.OperationName == "rss.cycle");
        var feeds = activities.Where(activity => activity.OperationName == "rss.feed").ToList();
        var publish = activities.Single(activity => activity.OperationName == "rss.publish");

        Assert.Multiple(() =>
        {
            Assert.That(cycle.Parent, Is.Null);
            Assert.That(feeds, Has.Count.EqualTo(2));
            Assert.That(feeds.All(feed => feed.Parent?.SpanId == cycle.SpanId), Is.True);
            Assert.That(publish.Parent?.SpanId, Is.EqualTo(cycle.SpanId));
            Assert.That(publish.GetTagItem("messaging.batch.size"), Is.EqualTo(4));
            Assert.That(cycle.GetTagItem("rss.items.published"), Is.EqualTo(4));
            Assert.That(cycle.GetTagItem("outcome"), Is.EqualTo("success"));
            // Per-item spans or events would make one cycle unbounded; only the bounded ones exist.
            Assert.That(activities.Any(activity => activity.OperationName.Contains("item", StringComparison.Ordinal)), Is.False);
            Assert.That(summary.ItemsPublished, Is.EqualTo(4));
        });
    }

    private static RssFeedReader CreateReader(FakeFeedClient client, RecordingQueue queue, params string[] feeds) =>
        new(
            new RssFeedReaderOptions
            {
                Feeds = feeds,
                Interval = TimeSpan.FromHours(1),
                RequestTimeout = TimeSpan.FromSeconds(10),
                MaxConcurrentFeeds = 4,
                MaxFeedBytes = RssFeedReaderOptions.DefaultMaxFeedBytes,
                MaxItemsPerFeed = RssFeedReaderOptions.DefaultMaxItemsPerFeed
            },
            client,
            queue,
            new RssFeedState(),
            NullLogger<RssFeedReader>.Instance,
            TimeProvider.System);

    private static RssFeedFetchResult Success(params string[] links) => new()
    {
        Outcome = RssFeedOutcome.Success,
        Links = links,
        ItemsParsed = links.Length,
        StatusCode = 200
    };

    private static RssFeedFetchResult SuccessWithValidator(string entityTag, params string[] links) => new()
    {
        Outcome = RssFeedOutcome.Success,
        Links = links,
        ItemsParsed = links.Length,
        EntityTag = entityTag,
        StatusCode = 200
    };

    private static RssFeedFetchResult Failed() => new()
    {
        Outcome = RssFeedOutcome.Failed,
        FailureReason = "http",
        StatusCode = 500
    };

    private sealed class FakeFeedClient : IRssFeedClient
    {
        public Func<string, RssFeedFetchResult> Respond { get; set; } = _ => Success();

        public List<string?> RequestedEntityTags { get; } = [];

        public Task<RssFeedFetchResult> FetchAsync(
            string feedUrl,
            string? entityTag,
            string? lastModified,
            CancellationToken cancellationToken)
        {
            RequestedEntityTags.Add(entityTag);
            return Task.FromResult(Respond(feedUrl));
        }
    }

    private sealed class RecordingQueue : IPgmqClient
    {
        public List<List<string>> Batches { get; } = [];

        public Exception? SendException { get; set; }

        public Task EnsureQueuesAsync(CancellationToken cancellationToken = default, params string[] additionalQueueNames) =>
            Task.CompletedTask;

        public Task<int> SendBatchAsync<T>(
            string queueName,
            IReadOnlyCollection<T> messages,
            CancellationToken cancellationToken = default)
        {
            if (SendException is not null)
            {
                throw SendException;
            }

            Batches.Add(messages.Cast<NewsfeedJobDescrpition>().Select(job => job.Url).ToList());
            return Task.FromResult(messages.Count);
        }

        public Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
            string queueName,
            int visibilityTimeoutSeconds,
            int quantity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PgmqMessage<T>>>([]);

        public Task<PgmqQueueMetrics> GetMetricsAsync(string queueName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PgmqQueueMetrics(queueName, 0, 0));

        public Task DeleteAsync(string queueName, long messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetVisibilityTimeoutAsync(
            string queueName,
            long messageId,
            int delaySeconds,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MoveAsync<T>(
            string sourceQueue,
            string destinationQueue,
            long messageId,
            T message,
            int delaySeconds = 0,
            IReadOnlyDictionary<string, string?>? headers = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
