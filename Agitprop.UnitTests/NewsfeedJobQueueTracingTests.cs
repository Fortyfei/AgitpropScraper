using System.Diagnostics;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Messaging;
using Agitprop.Scraper.Consumer.Consumers;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Agitprop.UnitTests;

public class NewsfeedJobQueueTracingTests
{
    [TestCase("success")]
    [TestCase("retry")]
    [TestCase("failed")]
    public async Task MessageSettlement_IsPartOfTheConsumerTrace(string outcome)
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(RecordingQueue.PollSourceName)
            .Build();
        using (var probe = RecordingQueue.PollSource.StartActivity("probe"))
        {
            Assert.That(probe, Is.Not.Null);
        }
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agitprop.NewsfeedJobConsumer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var parent = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        var traceParent = parent.Id!;
        var traceId = parent.TraceId;
        var parentSpanId = parent.SpanId;
        parent.Stop();

        var queue = new RecordingQueue(traceParent);
        var pipeline = new TestPipeline(outcome);
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPgmqClient>(queue)
            .AddSingleton<INewsfeedPipeline>(pipeline)
            .AddScoped<NewsfeedJobConsumer>()
            .BuildServiceProvider();
        using var worker = new NewsfeedJobQueueWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<NewsfeedJobQueueWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.Settled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var consume = activities.Single(activity => activity.OperationName == "Consume");
        Assert.Multiple(() =>
        {
            Assert.That(queue.Settlement, Is.EqualTo(outcome));
            Assert.That(consume.TraceId, Is.EqualTo(traceId));
            Assert.That(consume.ParentSpanId, Is.EqualTo(parentSpanId));
            Assert.That(queue.SettlementActivity, Is.Not.Null);
            Assert.That(queue.SettlementActivity?.SpanId, Is.EqualTo(consume.SpanId));
            Assert.That(queue.PollActivityCreated, Is.False);
            Assert.That(consume.Status, Is.EqualTo(outcome == "success"
                ? ActivityStatusCode.Ok : ActivityStatusCode.Error));
        });
    }

    [Test]
    public async Task LeaseRenewal_IsPartOfTheConsumerTrace()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agitprop.NewsfeedJobConsumer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        var traceParent = producer.Id!;
        producer.Stop();

        var queue = new RecordingQueue(traceParent);
        var pipeline = new BlockingPipeline();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPgmqClient>(queue)
            .AddSingleton<INewsfeedPipeline>(pipeline)
            .AddScoped<NewsfeedJobConsumer>()
            .BuildServiceProvider();
        using var worker = new NewsfeedJobQueueWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<NewsfeedJobQueueWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.LeaseRenewed.Task.WaitAsync(TimeSpan.FromSeconds(45));
            pipeline.Release.TrySetResult();
            await queue.Settled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            pipeline.Release.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }

        var consume = activities.Single(activity => activity.OperationName == "Consume");
        Assert.That(queue.LeaseActivity?.SpanId, Is.EqualTo(consume.SpanId));
        Assert.That(queue.SettlementActivity?.SpanId, Is.EqualTo(consume.SpanId));
    }

    private sealed class TestPipeline(string outcome) : INewsfeedPipeline
    {
        public INewsfeedSourceCatalog Sources { get; } = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

        public Task<IReadOnlyList<NewsfeedJobDescrpition>> CrawlAsync(
            NewsfeedJobDescrpition job, ISink? sinkOverride = null, CancellationToken cancellationToken = default)
        {
            if (outcome == "retry") throw new TimeoutException("transient");
            if (outcome == "failed") throw new ArgumentException("permanent");
            return Task.FromResult<IReadOnlyList<NewsfeedJobDescrpition>>([]);
        }
    }

    private sealed class BlockingPipeline : INewsfeedPipeline
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public INewsfeedSourceCatalog Sources { get; } = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

        public async Task<IReadOnlyList<NewsfeedJobDescrpition>> CrawlAsync(
            NewsfeedJobDescrpition job, ISink? sinkOverride = null, CancellationToken cancellationToken = default)
        {
            await Release.Task.WaitAsync(cancellationToken);
            return [];
        }
    }

    private sealed class RecordingQueue(string traceParent) : IPgmqClient
    {
        public const string PollSourceName = "Agitprop.Tests.QueuePoll";
        public static readonly ActivitySource PollSource = new(PollSourceName);
        private int _reads;
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LeaseRenewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Settlement { get; private set; }
        public Activity? SettlementActivity { get; private set; }
        public Activity? LeaseActivity { get; private set; }
        public bool PollActivityCreated { get; private set; }

        public Task EnsureQueuesAsync(CancellationToken cancellationToken = default, params string[] additionalQueueNames) =>
            Task.CompletedTask;

        public async Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
            string queueName, int visibilityTimeoutSeconds, int quantity, CancellationToken cancellationToken = default)
        {
            using var activity = PollSource.StartActivity("poll");
            PollActivityCreated |= activity is not null;
            if (Interlocked.Increment(ref _reads) == 1)
            {
                var message = new PgmqMessage<NewsfeedJobDescrpition>(
                    1, 1, new NewsfeedJobDescrpition { Url = "https://telex.hu/article", Type = PageContentType.Article },
                    new Dictionary<string, string?> { ["traceparent"] = traceParent });
                return [(PgmqMessage<T>)(object)message];
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }

        private Task RecordSettlement(string settlement)
        {
            Settlement = settlement;
            SettlementActivity = Activity.Current;
            Settled.TrySetResult();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string queueName, long messageId, CancellationToken cancellationToken = default) =>
            RecordSettlement("success");
        public Task SetVisibilityTimeoutAsync(string queueName, long messageId, int delaySeconds, CancellationToken cancellationToken = default)
        {
            if (delaySeconds == 120)
            {
                LeaseActivity = Activity.Current;
                LeaseRenewed.TrySetResult();
                return Task.CompletedTask;
            }
            return RecordSettlement("retry");
        }
        public Task MoveAsync<T>(string sourceQueue, string destinationQueue, long messageId, T message, int delaySeconds = 0,
            IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default) =>
            RecordSettlement("failed");
        public Task<long> SendAsync<T>(string queueName, T message, int delaySeconds = 0, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> SendBatchAsync<T>(string queueName, IReadOnlyCollection<T> messages, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
