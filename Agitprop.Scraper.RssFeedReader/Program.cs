using Agitprop.Messaging;

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
        builder.Configuration.AddUserSecrets<Program>();
        builder.AddServiceDefaults();
        builder.Services.AddHostedService<RssFeedReader>();
        builder.AddPgmqClient("newsfeed");

        var app = builder.Build();
        app.Run();
    }
}
