using System.Numerics;

namespace Agitprop.Scraper.RssFeedReader;

/// <summary>
/// Configuration of the RSS feed reader. It is loaded once at startup so a broken configuration
/// fails the host immediately instead of surfacing as a broken cycle an hour later.
/// </summary>
public sealed record RssFeedReaderOptions
{
    public const double DefaultIntervalMinutes = 60;
    public const double DefaultRequestTimeoutSeconds = 30;
    public const int DefaultMaxConcurrentFeeds = 4;
    public const long DefaultMaxFeedBytes = 5 * 1024 * 1024;
    public const int DefaultMaxItemsPerFeed = 500;

    public required IReadOnlyList<string> Feeds { get; init; }

    public required TimeSpan Interval { get; init; }

    public required TimeSpan RequestTimeout { get; init; }

    public required int MaxConcurrentFeeds { get; init; }

    public required long MaxFeedBytes { get; init; }

    public required int MaxItemsPerFeed { get; init; }

    public static RssFeedReaderOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();

        var feeds = ReadFeeds(configuration, errors);
        var intervalMinutes = configuration.GetValue("IntervalMinutes", DefaultIntervalMinutes);
        var requestTimeoutSeconds = configuration.GetValue("FeedRequestTimeoutSeconds", DefaultRequestTimeoutSeconds);
        var maxConcurrentFeeds = configuration.GetValue("MaxConcurrentFeeds", DefaultMaxConcurrentFeeds);
        var maxFeedBytes = configuration.GetValue("MaxFeedBytes", DefaultMaxFeedBytes);
        var maxItemsPerFeed = configuration.GetValue("MaxItemsPerFeed", DefaultMaxItemsPerFeed);

        RequirePositive(intervalMinutes, "IntervalMinutes", errors);
        RequirePositive(requestTimeoutSeconds, "FeedRequestTimeoutSeconds", errors);
        RequirePositive(maxConcurrentFeeds, "MaxConcurrentFeeds", errors);
        RequirePositive(maxFeedBytes, "MaxFeedBytes", errors);
        RequirePositive(maxItemsPerFeed, "MaxItemsPerFeed", errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid RSS feed reader configuration: " + string.Join(" ", errors));
        }

        return new RssFeedReaderOptions
        {
            Feeds = feeds,
            Interval = TimeSpan.FromMinutes(intervalMinutes),
            RequestTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds),
            MaxConcurrentFeeds = maxConcurrentFeeds,
            MaxFeedBytes = maxFeedBytes,
            MaxItemsPerFeed = maxItemsPerFeed
        };
    }

    private static List<string> ReadFeeds(IConfiguration configuration, List<string> errors)
    {
        var configuredFeeds = configuration.GetSection("Feeds").Get<string[]>();
        if (configuredFeeds is not { Length: > 0 })
        {
            errors.Add("'Feeds' must contain at least one RSS feed URL.");
            return [];
        }

        var feeds = new List<string>(configuredFeeds.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredFeed in configuredFeeds)
        {
            if (string.IsNullOrWhiteSpace(configuredFeed))
            {
                errors.Add("'Feeds' contains an empty entry.");
                continue;
            }

            if (!Uri.TryCreate(configuredFeed.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"Feed '{configuredFeed}' is not an absolute http(s) URL.");
                continue;
            }

            if (!seen.Add(uri.AbsoluteUri))
            {
                errors.Add($"Feed '{configuredFeed}' is configured more than once.");
                continue;
            }

            feeds.Add(uri.AbsoluteUri);
        }

        return feeds;
    }

    private static void RequirePositive<T>(T value, string key, List<string> errors)
        where T : INumber<T>
    {
        if (value <= T.Zero)
        {
            errors.Add($"'{key}' must be greater than zero.");
        }
    }
}
