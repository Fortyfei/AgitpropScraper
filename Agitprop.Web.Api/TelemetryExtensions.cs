namespace Agitprop.Web.Api;

/// <summary>
/// Provides extension methods for configuring OpenTelemetry tracing in the Web API.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing for the Web API service.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The updated web application builder.</returns>
    public static WebApplicationBuilder ConfigureWebApiTracing(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("Agitprop.NewsfeedSink")
                .AddSource("Agitprop.NewsfeedDB")
                .AddSource("Agitprop.Repository.EntityRepository")
                .AddSource("Agitprop.Repository.TrendingRepository")
            );

        return builder;
    }
}