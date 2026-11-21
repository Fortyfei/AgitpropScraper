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
                ["Feeds:0"] = "file:///agitprop-unit-test/missing-feed.xml"
            })
            .Build();
        var reader = new RssFeedReader(
            configuration,
            NullLogger<RssFeedReader>.Instance,
            new EmptyQueue());

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
            // The unreadable feed makes the cycle a partial failure instead of a success.
            Assert.That(cycles.All(cycle => Equals(cycle.GetTagItem("outcome"), "partial_failure")), Is.True);
        });
    }

    [Test]
    public void BatchedPublish_GivesEveryMessageItsOwnTraceContext()
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
            Assert.That(published.Select(activity => activity.TraceId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(published.All(activity => activity.TraceId != originTraceId), Is.True);
            Assert.That(published.All(activity => activity.Parent is null), Is.True);
            Assert.That(published.All(activity =>
                Equals(activity.GetTagItem("agitprop.batch.origin_trace_id"), originTraceId.ToHexString())), Is.True);
            Assert.That(stopped, Has.Count.EqualTo(3));
        });

        // The traceparent written to pgmq parses back as a remote parent, so consumption continues
        // the message trace instead of the producer's.
        Assert.That(ActivityContext.TryParse(traceParents[0], null, isRemote: true, out var remoteParent), Is.True);
        Assert.That(remoteParent.TraceId, Is.EqualTo(published[0].TraceId));
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

    private sealed class EmptyQueue : IPgmqClient
    {
        public Task EnsureQueuesAsync(CancellationToken cancellationToken = default, params string[] additionalQueueNames) =>
            Task.CompletedTask;

        public Task<long> SendAsync<T>(string queueName, T message, int delaySeconds = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(1L);

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
