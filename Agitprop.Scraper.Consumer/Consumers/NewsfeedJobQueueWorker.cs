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
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan QueueErrorDelay = TimeSpan.FromSeconds(5);

    private readonly IPgmqClient _pgmqClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewsfeedJobQueueWorker> _logger;
    private readonly int _concurrencyLimit;

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

                if (messages.Count == 0)
                {
                    await Task.Delay(EmptyQueueDelay, stoppingToken);
                    continue;
                }

                await Task.WhenAll(messages.Select(message => ProcessMessageAsync(message, stoppingToken)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                using var activity = ActivitySource.StartActivity("QueueWorkerError");
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                _logger.LogError(exception, "Failed while reading or processing the pgmq newsfeed queue");
                await Task.Delay(QueueErrorDelay, stoppingToken);
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
        catch (Exception exception)
        {
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
            return;
        }

        var failureKind = NewsfeedJobFailureClassifier.Classify(processingError, cancellationToken);
        var retryDelay = NewsfeedJobRetryPolicy.GetRetryDelay(message.ReadCount, failureKind);
        if (retryDelay is { } delay)
        {
            var delaySeconds = (int)Math.Ceiling(delay.TotalSeconds);
            _logger.LogWarning(
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

        _logger.LogError(
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
