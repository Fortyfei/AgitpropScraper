using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Agitprop.Consumer.Consumers;
using Agitprop.Scraper.Consumer.Consumers;

using MassTransit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;

using Polly;
using Polly.Retry;

using PuppeteerSharp;

namespace Agitprop.Scraper.Consumer;

/// <summary>
/// Provides extension methods for configuring application services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Configures MassTransit for message-based communication in the application.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigureMassTransit(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("messaging");
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.SetInMemorySagaRepositoryProvider();
            var entryAssembly = Assembly.GetEntryAssembly();
            x.AddConsumer<NewsfeedJobConsumer, NewsfeedJobConsumerDefinition>();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(connectionString, h =>
                {
                    h.Heartbeat(TimeSpan.FromSeconds(20));
                });

                cfg.ClearSerialization();
                cfg.AddRawJsonSerializer();
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    /// <summary>
    /// Configures resiliency pipelines for handling transient errors in the application.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigureResiliency(this IHostApplicationBuilder builder)
    {
        builder.Services.AddResiliencePipeline("Spider", static builder =>
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = args => args.Outcome switch
                {
                    { Exception: HttpRequestException } => PredicateResult.True(),
                    { Exception: TaskCanceledException } => PredicateResult.True(),
                    { Exception: TimeoutException } => PredicateResult.True(),
                    { Exception: NavigationException } => PredicateResult.True(),
                    { Result: HttpResponseMessage response } when !response.IsSuccessStatusCode => PredicateResult.True(),
                    _ => PredicateResult.False()
                },
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                MaxRetryAttempts = 15,
                UseJitter = false,
            });
        });
        builder.Services.AddResilienceEnricher();
        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry metrics for Aspire observability.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigureMetrics(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                // Built-in instrumentation for metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation()
            );

        return builder;
    }
}
