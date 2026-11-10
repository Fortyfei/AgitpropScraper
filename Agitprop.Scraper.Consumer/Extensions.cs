using Agitprop.Messaging;
using Agitprop.Scraper.Consumer.Consumers;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Hosting;

namespace Agitprop.Scraper.Consumer;

/// <summary>
/// Provides extension methods for configuring application services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Configures the PostgreSQL-backed newsfeed job queue.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigurePgmq(this IHostApplicationBuilder builder)
    {
        builder.AddPgmqClient("newsfeed");
        builder.Services.AddScoped<NewsfeedJobConsumer>();
        builder.Services.AddHostedService<NewsfeedJobQueueWorker>();
        return builder;
    }

}
