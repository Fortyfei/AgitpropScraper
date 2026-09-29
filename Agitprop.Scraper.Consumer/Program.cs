using System;

using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;

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

        // Add default service configurations.
        builder.AddServiceDefaults();

        // Configure infrastructure with browser support.
        builder.ConfigureInfrastructureWithBrowser(false);

        // Configure MassTransit for message-based communication.
        builder.ConfigureMassTransit();
        builder.ConfigureTracing();
        builder.ConfigureMetrics();

        // Add the Newsfeed sink for processing scraped data.
        builder.AddNewsfeedSink();

        builder.Configuration.AddUserSecrets<Program>();    
        
        var newsfeedConnectionString = builder.Configuration.GetConnectionString("newsfeed");
        if (string.IsNullOrWhiteSpace(newsfeedConnectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration key 'ConnectionStrings:newsfeed'. " +
                "Run this service through AppHost or set the ConnectionStrings__newsfeed environment variable.");
        }

        var messagingConnectionString = builder.Configuration.GetConnectionString("messaging");
        if (string.IsNullOrWhiteSpace(messagingConnectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration key 'ConnectionStrings:messaging'. " +
                "Run this service through AppHost or set the ConnectionStrings__messaging environment variable.");
        }

        var app = builder.Build();
        app.Services.GetRequiredService<INamedEntityRecognizer>();
        
        app.Run();
    }
}