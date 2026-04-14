using MassTransit;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// The entry point for the RssFeedReader application.
/// </summary>
public class Program
{
    /// <summary>
    /// The main method that configures and runs the application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddHostedService<RssFeedReader>();
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.SetInMemorySagaRepositoryProvider();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(builder.Configuration.GetConnectionString("messaging"));

                cfg.ClearSerialization();
                cfg.AddRawJsonSerializer();
                cfg.ConfigureEndpoints(context);
            });
        });


        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("Agitprop.RssFeedReader")
            );

        var app = builder.Build();
        app.Run();
    }
}
