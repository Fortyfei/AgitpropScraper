using System.Text.Json;
using Agitprop.Core.Enums;
using Agitprop.Messaging;
using Agitprop.Sinks.Newsfeed;
using Npgsql;

namespace Agitprop.CLI.Services;

public interface IArticleJobPublisher
{
    Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default);

    Task<RetryFailedFeedsExecutionResult> RetryFailedFeedsAsync(RetryFailedFeedsRequest request, CancellationToken cancellationToken = default);
}

public sealed class PgmqArticleJobPublisher : IArticleJobPublisher
{
    public async Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsPublishingEnabled)
        {
            return new PublishExecutionResult(true, 0, null, false);
        }

        var publishedCount = 0;

        try
        {
            await using var dataSource = NpgsqlDataSource.Create(request.ConnectionString!);
            var pgmqClient = new PgmqClient(dataSource);
            await pgmqClient.EnsureQueuesAsync(cancellationToken);
            var jobs = request.Articles.Select(article => new NewsfeedJobDescrpition
            {
                Url = article.Url,
                Type = PageContentType.Article
            }).ToList();
            publishedCount = await pgmqClient.SendBatchAsync(
                PgmqQueueNames.NewsfeedJobs,
                jobs,
                cancellationToken);

            return new PublishExecutionResult(true, publishedCount, null, true);
        }
        catch (Exception ex)
        {
            return new PublishExecutionResult(false, publishedCount, ex.Message, true);
        }
    }

    public async Task<RetryFailedFeedsExecutionResult> RetryFailedFeedsAsync(RetryFailedFeedsRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsRetryEnabled)
        {
            return new RetryFailedFeedsExecutionResult(true, 0, 0, null, false);
        }

        var failedQueueName = string.IsNullOrWhiteSpace(request.FailedQueueName)
            ? PgmqQueueNames.FailedNewsfeedJobs
            : request.FailedQueueName;
        var targetQueueName = string.IsNullOrWhiteSpace(request.TargetQueueName)
            ? PgmqQueueNames.NewsfeedJobs
            : request.TargetQueueName;

        var requeuedCount = 0;
        var scannedCount = 0;

        try
        {
            await using var dataSource = NpgsqlDataSource.Create(request.ConnectionString!);
            var pgmqClient = new PgmqClient(dataSource);
            await pgmqClient.EnsureQueuesAsync(
                cancellationToken,
                failedQueueName,
                targetQueueName);

            while (request.MaxMessages == 0 || scannedCount < request.MaxMessages)
            {
                var quantity = request.MaxMessages == 0
                    ? 50
                    : Math.Min(50, request.MaxMessages - scannedCount);
                var failedMessages = await pgmqClient.ReadAsync<JsonElement>(
                    failedQueueName,
                    visibilityTimeoutSeconds: 60,
                    quantity: quantity,
                    cancellationToken: cancellationToken);
                if (failedMessages.Count == 0)
                {
                    break;
                }

                foreach (var failedMessage in failedMessages)
                {
                    scannedCount++;
                    await pgmqClient.MoveAsync(
                        failedQueueName,
                        targetQueueName,
                        failedMessage.MessageId,
                        failedMessage.Message,
                        headers: failedMessage.Headers,
                        cancellationToken: cancellationToken);
                    requeuedCount++;
                }
            }

            return new RetryFailedFeedsExecutionResult(true, requeuedCount, scannedCount, null, true);
        }
        catch (Exception ex)
        {
            return new RetryFailedFeedsExecutionResult(false, requeuedCount, scannedCount, ex.Message, true);
        }
    }
}

public sealed class NoOpArticleJobPublisher : IArticleJobPublisher
{
    public Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PublishExecutionResult(true, 0, null, false));
    }

    public Task<RetryFailedFeedsExecutionResult> RetryFailedFeedsAsync(RetryFailedFeedsRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RetryFailedFeedsExecutionResult(true, 0, 0, null, false));
    }
}
