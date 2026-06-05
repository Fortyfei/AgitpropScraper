using System.Text;
using System.Text.Json;
using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed;
using RabbitMQ.Client;

namespace Agitprop.CLI.Services;

public interface IArticleJobPublisher
{
    Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqArticleJobPublisher : IArticleJobPublisher
{
    private const string QueueName = "newsfeed-job";

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
}

public sealed class NoOpArticleJobPublisher : IArticleJobPublisher
{
    public Task<PublishExecutionResult> PublishAsync(PublishArticlesRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PublishExecutionResult(true, 0, null, false));
    }
}
