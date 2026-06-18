using System.Text;
using System.Text.Json;
using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed;
using RabbitMQ.Client;

namespace Agitprop.CLI.Services;

public interface IArticleJobPublisher
{
    Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default);

    Task<RetryFailedFeedsExecutionResult> RetryFailedFeedsAsync(RetryFailedFeedsRequest request, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqArticleJobPublisher : IArticleJobPublisher
{
    private const string QueueName = "newsfeed-job";
    private const string FailedQueueName = "newsfeed-job_error";

    public async Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsPublishingEnabled)
        {
            return new PublishExecutionResult(true, 0, null, false);
        }

        var publishedCount = 0;

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(request.ConnectionString!)
            };

            using var connection = await factory.CreateConnectionAsync(cancellationToken);
            using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            foreach (var article in request.Articles)
            {
                var job = new NewsfeedJobDescrpition
                {
                    Url = article.Url,
                    Type = PageContentType.Article
                };

                var message = JsonSerializer.Serialize(job);
                var body = Encoding.UTF8.GetBytes(message);

                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: QueueName,
                    mandatory: true,
                    body: body,
                    cancellationToken: cancellationToken);

                publishedCount++;
            }

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

        var failedQueueName = string.IsNullOrWhiteSpace(request.FailedQueueName) ? FailedQueueName : request.FailedQueueName;
        var targetQueueName = string.IsNullOrWhiteSpace(request.TargetQueueName) ? QueueName : request.TargetQueueName;

        var requeuedCount = 0;
        var scannedCount = 0;

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(request.ConnectionString!)
            };

            using var connection = await factory.CreateConnectionAsync(cancellationToken);
            using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: failedQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: targetQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            while (request.MaxMessages == 0 || scannedCount < request.MaxMessages)
            {
                var failedMessage = await channel.BasicGetAsync(
                    queue: failedQueueName,
                    autoAck: false,
                    cancellationToken: cancellationToken);

                if (failedMessage is null)
                {
                    break;
                }

                scannedCount++;

                try
                {
                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: targetQueueName,
                        mandatory: true,
                        body: failedMessage.Body,
                        cancellationToken: cancellationToken);

                    await channel.BasicAckAsync(
                        deliveryTag: failedMessage.DeliveryTag,
                        multiple: false,
                        cancellationToken: cancellationToken);

                    requeuedCount++;
                }
                catch
                {
                    await channel.BasicNackAsync(
                        deliveryTag: failedMessage.DeliveryTag,
                        multiple: false,
                        requeue: true,
                        cancellationToken: cancellationToken);

                    throw;
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
