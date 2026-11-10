using System;

using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddUserSecrets<Program>();

        // Add default service configurations.
        builder.AddServiceDefaults();

        // Configure infrastructure with browser support.
        builder.ConfigureInfrastructureWithBrowser(false);

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
        
        app.Run();
    }
}