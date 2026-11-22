using System.Diagnostics;

using Agitprop.Messaging;
using Agitprop.Scraper.RssFeedReader;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class TelemetryTraceBoundaryTests
{
    [Test]
    public async Task RssCycles_StartTheirOwnTraceAndTheHostLifetimeKeepsNoSpan()
    {
        var activities = new List<Activity>();
        var stoppedCycles = 0;
        var twoCyclesStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agitprop.RssFeedReader",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                activities.Add(activity);
                if (activity.OperationName == "rss.cycle" && Interlocked.Increment(ref stoppedCycles) >= 2)
                {
                    twoCyclesStopped.TrySetResult();
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Two cycles are enough to show that neither inherits the other as a parent.
                ["IntervalMinutes"] = "0.001",
                ["Feeds:0"] = "https://example.invalid/working",
                ["Feeds:1"] = "https://example.invalid/broken"
            })
            .Build();
        var reader = new RssFeedReader(
            RssFeedReaderOptions.Load(configuration),
            new PartiallyFailingFeedClient(),
            new EmptyQueue(),
            new RssFeedState(),
            NullLogger<RssFeedReader>.Instance,
            TimeProvider.System);

        await reader.StartAsync(CancellationToken.None);
        try
        {
            await twoCyclesStopped.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            await reader.StopAsync(CancellationToken.None);
        }

        var cycles = activities.Where(activity => activity.OperationName == "rss.cycle").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(activities.Any(activity => activity.OperationName == "StartAsync"), Is.False);
            Assert.That(cycles, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(cycles.All(cycle => cycle.Parent is null), Is.True);
            Assert.That(cycles.Select(cycle => cycle.TraceId).Distinct().Count(), Is.EqualTo(cycles.Count));
            // One broken feed out of two makes the cycle a partial failure instead of a success.
            Assert.That(cycles.All(cycle => Equals(cycle.GetTagItem("outcome"), "partial_failure")), Is.True);
        });
    }

    [Test]
    public void BatchedPublish_KeepsEveryMessageInsideTheProducingTrace()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PgmqTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);

        using var origin = new Activity("rss.cycle").SetIdFormat(ActivityIdFormat.W3C).Start();
        var originTraceId = origin.TraceId;

        var published = new List<Activity>();
        var traceParents = new List<string?>();
        for (var index = 0; index < 3; index++)
        {
            using var scope = PgmqTelemetry.BeginPublish(PgmqQueueNames.NewsfeedJobs, batchSize: 3);
            Assert.That(scope.Activity, Is.Not.Null);
            published.Add(scope.Activity!);
            traceParents.Add(scope.Activity!.Id);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Activity.Current, Is.SameAs(origin), "The ambient activity must be restored");
            Assert.That(published.Select(activity => activity.TraceId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(published.All(activity => activity.TraceId == originTraceId), Is.True);
            Assert.That(published.All(activity => activity.ParentSpanId == origin.SpanId), Is.True);
            Assert.That(stopped, Has.Count.EqualTo(3));
        });

        // The traceparent written to pgmq parses back as a remote parent in the producer's trace, so
        // consumption continues that trace instead of starting one per message.
        Assert.That(ActivityContext.TryParse(traceParents[0], null, isRemote: true, out var remoteParent), Is.True);
        Assert.That(remoteParent.TraceId, Is.EqualTo(originTraceId));
    }

    [Test]
    public void PublishWithoutSourceListener_PreservesAmbientTraceContext()
    {
        using var origin = new Activity("rss.cycle").SetIdFormat(ActivityIdFormat.W3C).Start();

        using var scope = PgmqTelemetry.BeginPublish(PgmqQueueNames.NewsfeedJobs, batchSize: 1);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Activity, Is.Null);
            Assert.That(scope.PropagationActivity, Is.SameAs(origin));
        });
    }

    private sealed class PartiallyFailingFeedClient : IRssFeedClient
    {
        public Task<RssFeedFetchResult> FetchAsync(
            string feedUrl,
            string? entityTag,
            string? lastModified,
            CancellationToken cancellationToken) =>
            Task.FromResult(feedUrl.EndsWith("/working", StringComparison.Ordinal)
                ? new RssFeedFetchResult { Outcome = RssFeedOutcome.Success, StatusCode = 200 }
                : new RssFeedFetchResult
                {
                    Outcome = RssFeedOutcome.Failed,
                    FailureReason = "http",
                    StatusCode = 500
                });
    }

    private sealed class EmptyQueue : IPgmqClient
    {
        public Task EnsureQueuesAsync(CancellationToken cancellationToken = default, params string[] additionalQueueNames) =>
            Task.CompletedTask;

        public Task<int> SendBatchAsync<T>(string queueName, IReadOnlyCollection<T> messages, CancellationToken cancellationToken = default) =>
            Task.FromResult(messages.Count);

        public Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
            string queueName, int visibilityTimeoutSeconds, int quantity, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PgmqMessage<T>>>([]);

        public Task<PgmqQueueMetrics> GetMetricsAsync(string queueName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PgmqQueueMetrics(queueName, 0, 0));

        public Task DeleteAsync(string queueName, long messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetVisibilityTimeoutAsync(string queueName, long messageId, int delaySeconds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MoveAsync<T>(
            string sourceQueue, string destinationQueue, long messageId, T message, int delaySeconds = 0,
            IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
