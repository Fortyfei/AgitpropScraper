using System;
using System.Threading.Tasks;

using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agitprop.Scraper.Consumer;

/// <summary>
/// The entry point for the Agitprop Consumer application.
/// </summary>
public class Program
{
    /// <summary>
    /// The main method that configures and runs the application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddUserSecrets<Program>();

        // Add default service configurations.
        builder.AddServiceDefaults();

        // Configure infrastructure with browser support. Proxy rotation is opt-in:
        // Proxy:UseProxies=true routes HTTP page loads through the proxy pool.
        builder.ConfigureInfrastructureWithBrowser(
            builder.Configuration.GetValue("Proxy:UseProxies", false));

        builder.ConfigurePgmq();

        // Add the Newsfeed sink for processing scraped data.
        builder.AddNewsfeedSink();
        builder.AddNewsfeedSources();
        
        var newsfeedConnectionString = builder.Configuration.GetConnectionString("newsfeed");
        if (string.IsNullOrWhiteSpace(newsfeedConnectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration key 'ConnectionStrings:newsfeed'. " +
                "Run this service through AppHost or set the ConnectionStrings__newsfeed environment variable.");
        }

        var app = builder.Build();
        app.Services.GetRequiredService<INamedEntityRecognizer>();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Agitprop.Data.AppDbContext>();
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("EntityCatalog");
            await Agitprop.Data.EntityCatalogSeeder.TrySeedAsync(db, logger);
        }

        await app.RunAsync();
    }
}