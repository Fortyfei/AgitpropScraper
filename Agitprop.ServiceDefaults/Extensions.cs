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
    private static ExplicitBucketHistogramConfiguration ScraperDurationBuckets() => new()
    {
        Boundaries = [100, 1_000, 10_000, 30_000, 120_000, 300_000]
    };

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

        // A parent-based sampler keeps one decision per trace. Root spans are sampled by ratio so
        // background volume stays affordable; the roots that anchor a bounded trace (an RSS cycle
        // and a user journey) are always kept.
        var traceSampleRatio = Math.Clamp(
            builder.Configuration.GetValue("Observability:TraceSampleRatio", 1.0),
            0.0,
            1.0);

        // EF Core and Npgsql would each produce a database span for the same command, doubling
        // database spans and their attributes. EF Core is the layer kept by default because its
        // span names say which application operation issued the query; set
        // Observability:EnableNpgsqlTracing when raw command text is what you need.
        var enableNpgsqlTracing = builder.Configuration.GetValue("Observability:EnableNpgsqlTracing", false);

        ApplySpanAttributeLimits();

        var traceSources = new List<string>
        {
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
            "Agitprop.Messaging.PgmqPublisher",
            "Agitprop.Web.AnalyticsService",
            "Agitprop.Web.Journey"
        };

        if (enableNpgsqlTracing)
        {
            traceSources.Add("Npgsql");
        }

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
                       "Agitprop.Messaging",
                       "Agitprop.NewsfeedDB",
                       "Agitprop.Web")
                   // Backend default buckets cannot separate a warm page from a hung one; scraper
                   // work spans milliseconds to several minutes.
                   .AddView("newsfeed.job.duration", ScraperDurationBuckets())
                   .AddView("spider.processing.duration", ScraperDurationBuckets())
                   .AddView("rss.cycle.duration", ScraperDurationBuckets())
                   .AddView("rss.feed.duration", ScraperDurationBuckets())
                   .AddView("respectful.request.latency", ScraperDurationBuckets())
                   .AddView("respectful.rate_limit.wait_duration", ScraperDurationBuckets());
        })
        .WithTracing(builder =>
        {
            builder.SetSampler(new BoundedTraceSampler(traceSampleRatio))
                   .AddHttpClientInstrumentation()
                   // Blazor's own routing, event and circuit activities would each start their own
                   // trace. The web app groups a user interaction with Agitprop.Web.Journey instead,
                   // so every query of one interaction lands in one trace.
                   .AddAspNetCoreInstrumentation(options =>
                   {
                       options.EnableRazorComponentsSupport = false;
                       options.EnableAspNetCoreSignalRSupport = false;
                       options.Filter = context => !IsStaticAssetRequest(context.Request.Path);
                   })
                   // Only one database layer is traced by default. EF Core spans name the
                   // application operation that issued the query; Npgsql spans would only
                   // duplicate each command and are opt-in through
                   // Observability:EnableNpgsqlTracing. This is the one beta package left
                   // on the production path.
                   .AddEntityFrameworkCoreInstrumentation()
                   .AddSource(traceSources.ToArray());
        });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// The .NET SDK reads attribute and event limits from the environment when it creates the
    /// provider, and offers no programmatic equivalent. Defaults keep one noisy span from bloating
    /// an exporter batch without overriding a deployment that sets its own limit.
    /// </summary>
    private static void ApplySpanAttributeLimits()
    {
        SetDefaultEnvironmentVariable("OTEL_SPAN_ATTRIBUTE_COUNT_LIMIT", "64");
        SetDefaultEnvironmentVariable("OTEL_SPAN_ATTRIBUTE_VALUE_LENGTH_LIMIT", "1024");
        SetDefaultEnvironmentVariable("OTEL_SPAN_EVENT_COUNT_LIMIT", "16");
    }

    private static void SetDefaultEnvironmentVariable(string name, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
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
