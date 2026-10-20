using Agitprop.Core.Models;

namespace Agitprop.Core.Interfaces
{
    public interface IArticleFeedRepository
    {
        ArticleFeedPage GetArticleFeed(
            DateOnly from, DateOnly to, string? entityId, string? domain, string? search, int page, int pageSize);

        IReadOnlyList<DailyArticleCount> GetDailyArticleCounts(
            DateOnly from, DateOnly to, string? entityId, string? domain);

        IReadOnlyList<PublisherFirstSeen> GetPublisherFirstSeen(string entityId, DateOnly from, DateOnly to);
    }
}
