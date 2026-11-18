using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler(options =>
            {
                // Set the default timeout to 5 seconds
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                // Set the default retry count to 3
                options.Retry.MaxRetryAttempts = 3;
                // Set the default circuit breaker threshold to 50%
            });

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        {
            options.AllowedSchemes = ["https", "http"];
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
        });

        builder.Services.AddOpenTelemetry()
        .WithMetrics(builder =>
        {
            builder.AddHttpClientInstrumentation()
                   .AddAspNetCoreInstrumentation()
                   .AddRuntimeInstrumentation()
                   .AddMeter(
                       "Agitprop.Spider",
                       "Agitprop.NewsfeedJobConsumer",
                       "Agitprop.RssFeedReader",
                       "Agitprop.RespectfulPageRequester",
                       "Agitprop.Web");
        })
        .WithTracing(builder =>
        {
            builder.AddHttpClientInstrumentation()
                   // Blazor's own routing, event and circuit activities would each start their own
                   // trace. The web app groups a user interaction with Agitprop.Web.Journey instead,
                   // so every query of one interaction lands in one trace.
                   .AddAspNetCoreInstrumentation(options =>
                   {
                       options.EnableRazorComponentsSupport = false;
                       options.EnableAspNetCoreSignalRSupport = false;
                       options.Filter = context => !IsStaticAssetRequest(context.Request.Path);
                   })
                   // No stable release exists for the EF Core instrumentation, and the Npgsql
                   // traces alone would not show which query the application issued. This is the
                   // one beta package left on the production path.
                   .AddEntityFrameworkCoreInstrumentation()
                   .AddSource(
                       "Agitprop.Spider",
                       "Agitprop.PageLoader.HttpStaticPageLoader",
                       "Agitprop.PageLoader.PuppeteerPageLoader",
                       "Agitprop.PageLoader.PuppeteerPageLoaderWithProxies",
                       "Agitprop.PageRequester.RespectfulPageRequester",
                       "Agitprop.ProxyProviders.ProxyScrapeProxyProvider",
                       "Agitprop.ProxyProviders.RedScrapeProxyProvider",
                       "Agitprop.ProxyPool",
                       "Agitprop.RotatingHttpClientPool",
                       "Agitprop.RssFeedReader",
                       "Agitprop.NewsfeedJobConsumer",
                       "Agitprop.NewsfeedSink",
                       "Agitprop.NamedEntityRecognizer",
                       "Agitprop.NewsfeedDB",
                       "Agitprop.Repository.EntityRepository",
                       "Agitprop.Repository.TrendingRepository",
                       "Npgsql",
                       "Agitprop.Web.AnalyticsService",
                       "Agitprop.Web.Journey");
        });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Static files and the Blazor circuit handshake are not user journeys; tracing them only
    /// adds single-span traces next to the real one.
    /// </summary>
    private static bool IsStaticAssetRequest(PathString path)
    {
        var value = path.Value ?? string.Empty;

        return value.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints are Development-only unless explicitly enabled, because exposing them
        // in production has security implications. See https://aka.ms/dotnet/aspire/healthchecks.
        var exposeHealthEndpoints = app.Environment.IsDevelopment()
            || app.Configuration.GetValue<bool>("HealthEndpoints:Enabled");

        if (exposeHealthEndpoints)
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
