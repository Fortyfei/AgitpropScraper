using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agitprop.Messaging;

/// <summary>
/// Trace and metric instruments for pgmq.
/// </summary>
public static class PgmqTelemetry
{
    public const string ActivitySourceName = "Agitprop.Messaging.PgmqPublisher";
    public const string MeterName = "Agitprop.Messaging";

    private const string QueueTag = "messaging.destination.name";
    private const string FailureClassTag = "failure.class";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly ConcurrentDictionary<string, QueueSnapshot> SnapshotByQueue = new(StringComparer.Ordinal);

    private static readonly Counter<long> RetryCount = Meter.CreateCounter<long>(
        "pgmq.message.retries",
        description: "Messages scheduled for another delivery attempt, grouped by queue and bounded failure class");

    private static readonly Counter<long> DeadLetterCount = Meter.CreateCounter<long>(
        "pgmq.message.dead_lettered",
        description: "Messages moved to the failed queue, grouped by queue and bounded failure class");

    private static readonly Counter<long> LeaseRenewalFailureCount = Meter.CreateCounter<long>(
        "pgmq.lease.renewal.failures",
        description: "Visibility timeout renewals that failed, grouped by queue");

    private static readonly UpDownCounter<long> InFlightCount = Meter.CreateUpDownCounter<long>(
        "pgmq.messages.in_flight",
        description: "Messages claimed by the worker and not settled yet, grouped by queue");

    private static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge(
        "pgmq.queue.depth",
        ObserveQueueDepth,
        unit: "{message}",
        description: "Messages waiting in the queue, grouped by queue");

    private static readonly ObservableGauge<double> OldestMessageAge = Meter.CreateObservableGauge(
        "pgmq.queue.oldest_message_age",
        ObserveOldestMessageAge,
        unit: "s",
        description: "Age of the oldest message still waiting in the queue, grouped by queue");

    public static void RecordRetry(string queueName, string failureClass) =>
        RetryCount.Add(1, Tag(QueueTag, queueName), Tag(FailureClassTag, failureClass));

    public static void RecordDeadLetter(string queueName, string failureClass) =>
        DeadLetterCount.Add(1, Tag(QueueTag, queueName), Tag(FailureClassTag, failureClass));

    public static void RecordLeaseRenewalFailure(string queueName) =>
        LeaseRenewalFailureCount.Add(1, Tag(QueueTag, queueName));

    public static void AddInFlight(string queueName, long delta) =>
        InFlightCount.Add(delta, Tag(QueueTag, queueName));

    public static void ReportQueueMetrics(string queueName, long queueLength, double oldestMessageAgeSeconds) =>
        SnapshotByQueue[queueName] = new QueueSnapshot(queueLength, oldestMessageAgeSeconds);

    /// <summary>
    /// Starts the producer span for one message. The span is a child of the current activity, so a
    /// batch published by one application operation (an RSS cycle, a crawl) stays inside that
    /// operation's trace, and the consumer continues the same trace through the traceparent stored
    /// in the pgmq headers. Without an ambient activity the span becomes a root of its own.
    /// </summary>
    public static PublishScope BeginPublish(string queueName, int batchSize)
    {
        var tags = new ActivityTagsCollection
        {
            { QueueTag, queueName },
            { "messaging.system", "pgmq" },
            { "messaging.batch.size", batchSize }
        };

        var activity = ActivitySource.StartActivity(
            "publish",
            ActivityKind.Producer,
            Activity.Current?.Context ?? default,
            tags);

        return new PublishScope(activity);
    }

    private static IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        foreach (var (queueName, snapshot) in SnapshotByQueue)
        {
            yield return new Measurement<long>(snapshot.QueueLength, Tag(QueueTag, queueName));
        }
    }

    private static IEnumerable<Measurement<double>> ObserveOldestMessageAge()
    {
        foreach (var (queueName, snapshot) in SnapshotByQueue)
        {
            yield return new Measurement<double>(snapshot.OldestMessageAgeSeconds, Tag(QueueTag, queueName));
        }
    }

    private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);

    private readonly record struct QueueSnapshot(long QueueLength, double OldestMessageAgeSeconds);

    /// <summary>
    /// Holds one message publish span. Disposing it stops the span and restores the ambient activity
    /// it was created under.
    /// </summary>
    public readonly struct PublishScope : IDisposable
    {
        private readonly bool _started;

        internal PublishScope(Activity? activity)
        {
            Activity = activity;
            _started = true;
        }

        public Activity? Activity { get; }

        /// <summary>
        /// The context written into the pgmq headers: the publish span when a listener is recording,
        /// otherwise the ambient activity, so the consumer still continues the caller's trace.
        /// </summary>
        public Activity? PropagationActivity => Activity ?? System.Diagnostics.Activity.Current;

        public void Dispose()
        {
            if (!_started)
            {
                return;
            }

            Activity?.Dispose();
        }
    }
}


