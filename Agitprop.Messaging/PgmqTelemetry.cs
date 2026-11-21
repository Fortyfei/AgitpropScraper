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
    /// Starts the producer span for one message. The span is a new trace root so a batch of jobs is
    /// exported as one trace per message instead of a single trace holding every job. The producing
    /// context is recorded as an attribute because .NET activities cannot carry span links.
    /// </summary>
    public static PublishScope BeginPublish(string queueName, int batchSize)
    {
        var origin = Activity.Current;

        // Clearing the ambient activity makes the publish span a root. It is restored on dispose so
        // the enclosing cycle keeps whatever trace it had.
        Activity.Current = null;

        var tags = new ActivityTagsCollection
        {
            { QueueTag, queueName },
            { "messaging.system", "pgmq" },
            { "messaging.batch.size", batchSize }
        };

        if (origin is not null)
        {
            tags["agitprop.batch.origin_trace_id"] = origin.TraceId.ToHexString();
        }

        var activity = ActivitySource.StartActivity("publish", ActivityKind.Producer, default(ActivityContext), tags);

        if (activity is null)
        {
            System.Diagnostics.Activity.Current = origin;
        }

        return new PublishScope(activity, origin);
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
    /// Holds one message publish span and restores the ambient activity it replaced.
    /// </summary>
    public readonly struct PublishScope : IDisposable
    {
        private readonly bool _started;

        internal PublishScope(Activity? activity, Activity? origin)
        {
            Activity = activity;
            Origin = origin;
            _started = true;
        }

        public Activity? Activity { get; }

        public Activity? PropagationActivity => Activity ?? Origin;

        private Activity? Origin { get; }

        public void Dispose()
        {
            if (!_started)
            {
                return;
            }

            Activity?.Dispose();
            System.Diagnostics.Activity.Current = Origin;
        }
    }
}


