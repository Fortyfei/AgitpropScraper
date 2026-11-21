using System.Diagnostics;
using System.Diagnostics.Metrics;

using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Messaging;
using Agitprop.Scraper.Consumer.Consumers;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

public class NewsfeedJobQueueTelemetryTests
{
    [TestCase("transient", "pgmq.message.retries", "retry_scheduled", "transient")]
    [TestCase("permanent", "pgmq.message.dead_lettered", "dead_lettered", "permanent")]
    public async Task QueueFailureHandling_RecordsBoundedMetricsAndOutcome(
        string failure,
        string expectedInstrument,
        string expectedOutcome,
        string expectedFailureClass)
    {
        var measurements = new List<(string Instrument, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PgmqTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray())));
        meterListener.Start();

        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Agitprop.NewsfeedJobConsumer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(activityListener);

        var queue = new SettlementQueue("00-11111111111111111111111111111111-2222222222222222-01");
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPgmqClient>(queue)
            .AddSingleton<INewsfeedPipeline>(new FailingPipeline(failure))
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
            Assert.That(measurements.Any(measurement =>
                measurement.Instrument == expectedInstrument
                && measurement.Tags.Any(tag => tag.Key == "failure.class" && Equals(tag.Value, expectedFailureClass))
                && measurement.Tags.Any(tag => tag.Key == "messaging.destination.name")), Is.True);
            Assert.That(measurements.Any(measurement =>
                measurement.Instrument == "pgmq.messages.in_flight"), Is.True);
            Assert.That(consume.GetTagItem("outcome"), Is.EqualTo(expectedOutcome));
            Assert.That(consume.GetTagItem("failure.class"), Is.EqualTo(expectedFailureClass));
            Assert.That(consume.GetTagItem("job.url"), Is.Null);
        });
    }

    [Test]
    public void QueueDepthAndOldestMessageAge_AreExportedAsGaugesPerQueue()
    {
        var readings = new List<(string Instrument, object? Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PgmqTelemetry.MeterName
                    && instrument.Name.StartsWith("pgmq.queue.", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            readings.Add((instrument.Name, value, tags.ToArray())));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            readings.Add((instrument.Name, value, tags.ToArray())));
        meterListener.Start();

        PgmqTelemetry.ReportQueueMetrics(PgmqQueueNames.NewsfeedJobs, queueLength: 7, oldestMessageAgeSeconds: 12.5);
        meterListener.RecordObservableInstruments();

        Assert.Multiple(() =>
        {
            Assert.That(readings.Any(reading => reading.Instrument == "pgmq.queue.depth"
                && Equals(reading.Value, 7L)
                && reading.Tags.Any(tag => tag.Key == "messaging.destination.name"
                    && Equals(tag.Value, PgmqQueueNames.NewsfeedJobs))), Is.True);
            Assert.That(readings.Any(reading => reading.Instrument == "pgmq.queue.oldest_message_age"
                && Equals(reading.Value, 12.5)), Is.True);
        });
    }

    private sealed class FailingPipeline(string failure) : INewsfeedPipeline
    {
        public INewsfeedSourceCatalog Sources { get; } = new NewsfeedSourceCatalog(NewsfeedSources.BuiltIn());

        public Task<IReadOnlyList<NewsfeedJobDescrpition>> CrawlAsync(
            NewsfeedJobDescrpition job, ISink? sinkOverride = null, CancellationToken cancellationToken = default)
        {
            if (failure == "transient")
            {
                throw new TimeoutException("transient");
            }

            throw new ArgumentException("permanent");
        }
    }

    private sealed class SettlementQueue(string traceParent) : IPgmqClient
    {
        private int _reads;

        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureQueuesAsync(CancellationToken cancellationToken = default, params string[] additionalQueueNames) =>
            Task.CompletedTask;

        public async Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
            string queueName, int visibilityTimeoutSeconds, int quantity, CancellationToken cancellationToken = default)
        {
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

        public Task<PgmqQueueMetrics> GetMetricsAsync(string queueName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PgmqQueueMetrics(queueName, 0, 0));

        public Task DeleteAsync(string queueName, long messageId, CancellationToken cancellationToken = default) =>
            Settle();

        public Task SetVisibilityTimeoutAsync(
            string queueName, long messageId, int delaySeconds, CancellationToken cancellationToken = default) =>
            delaySeconds == 120 ? Task.CompletedTask : Settle();

        public Task MoveAsync<T>(
            string sourceQueue, string destinationQueue, long messageId, T message, int delaySeconds = 0,
            IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default) =>
            Settle();

        public Task<long> SendAsync<T>(
            string queueName, T message, int delaySeconds = 0, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SendBatchAsync<T>(
            string queueName, IReadOnlyCollection<T> messages, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private Task Settle()
        {
            Settled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
