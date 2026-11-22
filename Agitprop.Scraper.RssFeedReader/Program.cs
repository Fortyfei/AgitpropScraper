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

        // A broken feed configuration must fail the host at startup, not an hour later mid-cycle.
        var options = RssFeedReaderOptions.Load(builder.Configuration);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RssFeedState>();
        builder.Services.AddHttpClient(RssFeedClient.HttpClientName, httpClient =>
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AgitpropRssReader/1.0 (+https://agitprop.cc)"));
        builder.Services.AddSingleton<IRssFeedClient>(services => new RssFeedClient(
            services.GetRequiredService<IHttpClientFactory>(),
            options));
        builder.Services.AddHostedService<RssFeedReader>();
        builder.AddPgmqClient("newsfeed");

        var app = builder.Build();
        app.Run();
    }
}
