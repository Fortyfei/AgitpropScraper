using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Agitprop.Core.Enums;
using MassTransit;
using System.Diagnostics;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// A hosted service that reads RSS feeds and publishes scraping jobs.
/// </summary>
public class RssFeedReader : IHostedService, IDisposable
{
    private readonly string[] _feeds;
    private readonly ILogger<RssFeedReader> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private static readonly ActivitySource _activitySource = new("Agitprop.RssFeedReader");

    public RssFeedReader(IConfiguration configuration, ILogger<RssFeedReader> logger, IServiceScopeFactory scopeFactory)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _logger = logger;
        _feeds = configuration.GetSection("Feeds").Get<string[]>() ?? throw new ArgumentException("Feeds are not defined");
        _scopeFactory = scopeFactory;
        _interval = TimeSpan.FromMinutes(configuration.GetValue<double>("IntervalMinutes", 60));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("StartAsync");
        _logger.LogInformation("Starting RSS Feed Reader. Interval: {Interval} minutes, Feeds: {FeedCount}", _interval.TotalMinutes, _feeds.Length);
        var timer = new Timer(async _ => await ExecuteTask(_), null,TimeSpan.Zero, _interval);
        return;
    }

    private async Task ExecuteTask(object? state)
    {
        using var activity = _activitySource.StartActivity("ExecuteTask", ActivityKind.Producer);
        _logger.LogInformation("Running RSS feed scraping task");

        using var scope = _scopeFactory.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        try
        {
            var jobs = FetchScrapingJobs();
            if (jobs.Count == 0)
            {
                _logger.LogInformation("No new jobs fetched in this cycle");
            }
            else
            {
                _logger.LogInformation("Publishing {JobCount} scraping jobs", jobs.Count);
                await publishEndpoint.PublishBatch(jobs);
            }

            activity?.SetStatus(ActivityStatusCode.Ok, "RSS feed task completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while publishing scraping jobs");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("StopAsync");
        _logger.LogInformation("Stopping RSS Feed Reader");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing RSS Feed Reader resources");
    }
    
    private List<NewsfeedJobDescrpition> FetchScrapingJobs()
    {
        using var activity = _activitySource.StartActivity("FetchScrapingJobs", ActivityKind.Producer);
        var scrapingJobs = new List<NewsfeedJobDescrpition>();

        foreach (var feedUrl in _feeds)
        {
            using var feedActivity = _activitySource.StartActivity("ProcessFeed", ActivityKind.Consumer);
            feedActivity?.SetTag("feed.url", feedUrl);
            _logger.LogDebug("Reading RSS feed: {FeedUrl}", feedUrl);

            try
            {
                using var reader = XmlReader.Create(feedUrl);
                var feed = SyndicationFeed.Load(reader);

                if (feed != null)
                {
                    var news = feed.Items.Select(item =>
                    {
                        var link = item.Links.FirstOrDefault()?.Uri?.GetLeftPart(UriPartial.Path);
                        feedActivity?.AddEvent(new ActivityEvent("FeedItemRead", default, new ActivityTagsCollection
                        {
                            { "item.title", item.Title.Text },
                            { "item.link", link ?? "null" }
                        }));

                        return new NewsfeedJobDescrpition
                        {
                            Url = link ?? throw new ArgumentException("No link found"),
                            Type = PageContentType.Article
                        };
                    }).ToList();

                    _logger.LogInformation("Fetched {ItemCount} items from feed {FeedUrl}", news.Count, feedUrl);
                    scrapingJobs.AddRange(news);
                }
                else
                {
                    _logger.LogWarning("RSS feed {FeedUrl} returned no items", feedUrl);
                }

                feedActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing feed {FeedUrl}", feedUrl);
                feedActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }

        _logger.LogInformation("Total scraping jobs fetched: {JobCount}", scrapingJobs.Count);
        activity?.SetStatus(ActivityStatusCode.Ok, "FetchScrapingJobs completed");
        return scrapingJobs;
    }
}
