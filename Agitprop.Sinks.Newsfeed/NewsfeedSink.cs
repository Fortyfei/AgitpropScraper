using Polly;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

using Agitprop.Core;
using Agitprop.Core.Interfaces;

using Microsoft.Extensions.Logging;

namespace Agitprop.Sinks.Newsfeed;

public class NewsfeedSink : ISink
{
    private readonly INamedEntityRecognizer _nerService;
    private readonly INewsfeedDB _db;
    private readonly ILogger<NewsfeedSink> _logger;
    private static readonly ActivitySource _activitySource = new("Agitprop.NewsfeedSink");
    private readonly int _retryCount;

    public NewsfeedSink(INamedEntityRecognizer nerService, INewsfeedDB db, ILogger<NewsfeedSink> logger, IConfiguration? configuration = null)
    {
        _nerService = nerService;
        _db = db;
        _logger = logger;
        _retryCount = configuration?.GetValue<int>("Retry:NewsfeedSink", 3) ?? 3;
    }

    public async Task<bool> CheckPageAlreadyVisited(string url)
    {
        using var trace = _activitySource.StartActivity("CheckPageAlreadyVisited", ActivityKind.Internal);
        trace?.SetTag("article.url", TelemetryUrl.RedactQueryAndFragment(url));
        try
        {
            _logger?.LogDebug("Checking if page already visited: {Url}", TelemetryUrl.RedactQueryAndFragment(url));

            var exists = await Polly.Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(_retryCount, attempt => TimeSpan.FromSeconds(0.5 * attempt), (ex, ts, attempt, ctx) =>
                {
                    _logger?.LogWarning(ex, "[RETRY] Exception checking page {Url} on attempt {Attempt}",
                        TelemetryUrl.RedactQueryAndFragment(url), attempt);
                })
                .ExecuteAsync(() => _db.IsUrlAlreadyExists(url));

            _logger?.LogDebug("CheckPageAlreadyVisited result for {Url}: {Exists}",
                TelemetryUrl.RedactQueryAndFragment(url), exists);
            trace?.SetStatus(ActivityStatusCode.Ok);
            return exists;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to check if page already visited: {Url}",
                TelemetryUrl.RedactQueryAndFragment(url));
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
    {
        using var trace = _activitySource.StartActivity("EmitAsync", ActivityKind.Internal);
        trace?.SetTag("article.url", TelemetryUrl.RedactQueryAndFragment(url));
        trace?.SetTag("article.count", data.Count);
        _logger?.LogDebug("Processing {ArticleCount} articles for {Url}",
            data.Count, TelemetryUrl.RedactQueryAndFragment(url));

        foreach (var article in data)
        {
            trace?.SetTag("articleLength", article.Text.Length);

            try
            {
                var entities = await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(_retryCount, attempt => TimeSpan.FromSeconds(0.5 * attempt), (ex, ts, attempt, ctx) =>
                    {
                        _logger?.LogWarning(ex, "[RETRY] Exception analyzing entities for {Url} on attempt {Attempt}",
                            TelemetryUrl.RedactQueryAndFragment(url), attempt);
                    })
                    .ExecuteAsync(() => _nerService.AnalyzeSingleAsync(article.Text));

                _logger?.LogDebug("Received {EntityCount} entities for article in {Url}",
                    entities.All.Count, TelemetryUrl.RedactQueryAndFragment(url));

                var count = await Polly.Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(_retryCount, attempt => TimeSpan.FromSeconds(0.5 * attempt), (ex, ts, attempt, ctx) =>
                    {
                        _logger?.LogWarning(ex, "[RETRY] Exception inserting mentions for {Url} on attempt {Attempt}",
                            TelemetryUrl.RedactQueryAndFragment(url), attempt);
                    })
                    .ExecuteAsync(() => _db.CreateMentionsAsync(url, article, entities));

                _logger?.LogDebug("Inserted {MentionCount} mentions for article in {Url}",
                    count, TelemetryUrl.RedactQueryAndFragment(url));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process article for {Url}", TelemetryUrl.RedactQueryAndFragment(url));
                trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        _logger?.LogInformation("Finished processing {ArticleCount} articles for {Url}",
            data.Count, TelemetryUrl.RedactQueryAndFragment(url));
        trace?.SetStatus(ActivityStatusCode.Ok);
    }
}
