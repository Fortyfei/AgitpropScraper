namespace Agitprop.Core.Models;

public sealed record FeedEntity(string Id, string Name, string Type);

public sealed record ArticleFeedItem(
    string Id,
    string Title,
    string Url,
    string Domain,
    DateTime PublishedTime,
    IReadOnlyList<FeedEntity> Entities);

public sealed record ArticleFeedPage(IReadOnlyList<ArticleFeedItem> Items, int TotalCount);

public sealed record DailyArticleCount(DateOnly Date, int Count, bool IsSpike = false);

public sealed record PublisherFirstSeen(string Domain, DateTime FirstPublished, int ArticleCount);

public static class SpikeDetector
{
    private const int MinimumDays = 5;
    private const int MinimumSpikeCount = 3;
    private const double StandardDeviations = 2;

    // A spike is a day well above the window's average; short windows are too noisy to judge.
    public static IReadOnlyList<DailyArticleCount> Mark(IReadOnlyList<DailyArticleCount> days)
    {
        if (days.Count < MinimumDays)
            return days;

        var mean = days.Average(day => day.Count);
        var variance = days.Average(day => Math.Pow(day.Count - mean, 2));
        var threshold = mean + StandardDeviations * Math.Sqrt(variance);

        return days
            .Select(day => day with { IsSpike = day.Count >= MinimumSpikeCount && day.Count > threshold })
            .ToList();
    }
}
