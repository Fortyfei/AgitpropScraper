using System;
using System.Reflection;

using Agitprop.Consumer.Consumers;
using Agitprop.Scraper.Consumer.Consumers;

using MassTransit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    /// Configures OpenTelemetry tracing for the consumer service.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigureTracing(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("Agitprop.NewsfeedJobConsumer")
            );

        return builder;
    }

    public static IHostApplicationBuilder ConfigureMetrics(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("Agitprop.NewsfeedJobConsumer")
            );

        return builder;
    }
}
