using System;

using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
using Agitprop.Sinks.Newsfeed.Database;

using Microsoft.EntityFrameworkCore;
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

        // Configure infrastructure with browser support.
        builder.ConfigureInfrastructureWithBrowser(false);

        // Configure MassTransit for message-based communication.
        builder.ConfigureMassTransit();
        builder.ConfigureResiliency();
        builder.ConfigureMetrics();

        // Add the Newsfeed sink for processing scraped data.
        builder.AddNewsfeedSink();

        // Add default service configurations.
        builder.AddServiceDefaults();

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        var app = builder.Build();

        if (builder.Environment.IsDevelopment()
            || builder.Configuration.GetValue<bool>("ApplyMigrationsAtStartup"))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            Console.WriteLine("!!!!!!!!!!Applied migrations at startup!!!!!!!!!!");
        }
        
        app.Run();
    }
}