using System;
using System.Collections.Generic;
using System.Diagnostics;
using Agitprop.Messaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Agitprop.Sinks.Newsfeed;
using OpenTelemetry;

namespace Agitprop.Scraper.Consumer.Consumers;

public sealed class NewsfeedJobQueueWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Agitprop.NewsfeedJobConsumer");
    private const int MessageVisibilityTimeoutSeconds = 120;
    private const int QueueMetricsPollInterval = 10;
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan QueueErrorDelay = TimeSpan.FromSeconds(5);
    private static readonly string[] MetricQueues =
        new[] { PgmqQueueNames.NewsfeedJobs, PgmqQueueNames.FailedNewsfeedJobs };
    private static readonly EventId QueueReadFailedEvent = new(1001, "QueueReadFailed");
    private static readonly EventId RetryScheduledEvent = new(1002, "JobRetryScheduled");
    private static readonly EventId JobFailedPermanentlyEvent = new(1003, "JobFailedPermanently");

    private readonly IPgmqClient _pgmqClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewsfeedJobQueueWorker> _logger;
    private readonly int _concurrencyLimit;
    private int _pollsSinceMetrics;

    public NewsfeedJobQueueWorker(
        IPgmqClient pgmqClient,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<NewsfeedJobQueueWorker> logger)
    {
        _pgmqClient = pgmqClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _concurrencyLimit = configuration.GetValue<int?>("ConcurrencyLimit") ?? 1;
        if (_concurrencyLimit <= 0)
        {
            throw new InvalidOperationException("ConcurrencyLimit must be greater than zero.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (ActivitySource.StartActivity("InitializeQueue"))
        {
            await _pgmqClient.EnsureQueuesAsync(stoppingToken);
        }
        _logger.LogInformation(
            "Starting pgmq consumer for {QueueName} with concurrency limit {ConcurrencyLimit}",
            PgmqQueueNames.NewsfeedJobs,
            _concurrencyLimit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<PgmqMessage<NewsfeedJobDescrpition>> messages;
                using (SuppressInstrumentationScope.Begin())
                {
                    messages = await _pgmqClient.ReadAsync<NewsfeedJobDescrpition>(
                        PgmqQueueNames.NewsfeedJobs,
                        MessageVisibilityTimeoutSeconds,
                        _concurrencyLimit,
                        stoppingToken);
                }

                await ReportQueueMetricsAsync(stoppingToken);

                if (messages.Count == 0)
                {
                    await Task.Delay(EmptyQueueDelay, stoppingToken);
                    continue;
                }

                PgmqTelemetry.AddInFlight(PgmqQueueNames.NewsfeedJobs, messages.Count);
                try
                {
                    await Task.WhenAll(messages.Select(message => ProcessMessageAsync(message, stoppingToken)));
                }
                finally
                {
                    PgmqTelemetry.AddInFlight(PgmqQueueNames.NewsfeedJobs, -messages.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                using var activity = ActivitySource.StartActivity("QueueWorkerError");
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                _logger.LogError(
                    QueueReadFailedEvent,
                    exception,
                    "Failed while reading or processing the pgmq newsfeed queue");
                await Task.Delay(QueueErrorDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Queue depth and oldest-message age are scraped through pgmq and exported as gauges, because
    /// per-message queue logs are too noisy to alert on. The scrape runs without tracing so the poll
    /// loop does not create a database span every half second.
    /// </summary>
    private async Task ReportQueueMetricsAsync(CancellationToken cancellationToken)
    {
        if (++_pollsSinceMetrics < QueueMetricsPollInterval)
        {
            return;
        }

        _pollsSinceMetrics = 0;

        foreach (var queueName in MetricQueues)
        {
            try
            {
                PgmqQueueMetrics metrics;
                using (SuppressInstrumentationScope.Begin())
                {
                    metrics = await _pgmqClient.GetMetricsAsync(queueName, cancellationToken);
                }

                PgmqTelemetry.ReportQueueMetrics(queueName, metrics.QueueLength, metrics.OldestMessageAgeSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not read metrics for pgmq queue {QueueName}", queueName);
            }
        }
    }

    private async Task ProcessMessageAsync(
        PgmqMessage<NewsfeedJobDescrpition> message,
        CancellationToken cancellationToken)
    {
        ActivityContext.TryParse(
            message.Headers?.GetValueOrDefault("traceparent"),
            message.Headers?.GetValueOrDefault("tracestate"),
            isRemote: true,
            out var parentContext);
        using var activity = ActivitySource.StartActivity("Consume", ActivityKind.Consumer, parentContext);
        activity?.SetTag("messaging.destination.name", PgmqQueueNames.NewsfeedJobs);
        activity?.SetTag("messaging.message.id", message.MessageId.ToString());
        try
        {
            await ProcessMessageCoreAsync(message, activity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("outcome", "cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("outcome", "failure");
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }

    private async Task ProcessMessageCoreAsync(
        PgmqMessage<NewsfeedJobDescrpition> message,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        Exception? processingError = null;
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseRenewal = RenewLeaseAsync(message, leaseCancellation.Token);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var consumer = scope.ServiceProvider.GetRequiredService<NewsfeedJobConsumer>();
            await consumer.Consume(message, activity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            processingError = exception;
        }
        finally
        {
            leaseCancellation.Cancel();
            await leaseRenewal;
        }

        if (processingError is null)
        {
            await _pgmqClient.DeleteAsync(
                PgmqQueueNames.NewsfeedJobs,
                message.MessageId,
                cancellationToken);
            activity?.SetTag("outcome", "success");
            return;
        }

        var failureKind = NewsfeedJobFailureClassifier.Classify(processingError, cancellationToken);
        var failureClass = failureKind.ToString().ToLowerInvariant();
        var retryDelay = NewsfeedJobRetryPolicy.GetRetryDelay(message.ReadCount, failureKind);
        if (retryDelay is { } delay)
        {
            var delaySeconds = (int)Math.Ceiling(delay.TotalSeconds);
            activity?.SetTag("outcome", "retry_scheduled");
            activity?.SetTag("failure.class", failureClass);
            PgmqTelemetry.RecordRetry(PgmqQueueNames.NewsfeedJobs, failureClass);
            _logger.LogWarning(
                RetryScheduledEvent,
                processingError,
                "Scheduling pgmq message {MessageId} for retry in {DelaySeconds} seconds; attempt {Attempt}",
                message.MessageId,
                delaySeconds,
                message.ReadCount);
            await _pgmqClient.SetVisibilityTimeoutAsync(
                PgmqQueueNames.NewsfeedJobs,
                message.MessageId,
                delaySeconds,
                cancellationToken);
            return;
        }

        activity?.SetTag("outcome", "dead_lettered");
        activity?.SetTag("failure.class", failureClass);
        PgmqTelemetry.RecordDeadLetter(PgmqQueueNames.NewsfeedJobs, failureClass);
        _logger.LogError(
            JobFailedPermanentlyEvent,
            processingError,
            "Moving pgmq message {MessageId} to the failed queue after attempt {Attempt}",
            message.MessageId,
            message.ReadCount);
        await _pgmqClient.MoveAsync(
            PgmqQueueNames.NewsfeedJobs,
            PgmqQueueNames.FailedNewsfeedJobs,
            message.MessageId,
            message.Message,
            headers: message.Headers,
            cancellationToken: cancellationToken);
    }

    private async Task RenewLeaseAsync(
        PgmqMessage<NewsfeedJobDescrpition> message,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(LeaseRenewalInterval, cancellationToken);
                try
                {
                    await _pgmqClient.SetVisibilityTimeoutAsync(
                        PgmqQueueNames.NewsfeedJobs,
                        message.MessageId,
                        MessageVisibilityTimeoutSeconds,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    PgmqTelemetry.RecordLeaseRenewalFailure(PgmqQueueNames.NewsfeedJobs);
                    _logger.LogWarning(
                        exception,
                        "Could not renew visibility timeout for pgmq message {MessageId}",
                        message.MessageId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
