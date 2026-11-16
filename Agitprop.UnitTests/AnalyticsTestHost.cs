using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Agitprop.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.UnitTests;

internal static class AnalyticsTestHost
{
    public static AnalyticsService Create(
        IEntityRepository? entityRepository = null,
        ITrendingRepository? trendingRepository = null,
        IArticleFeedRepository? articleFeedRepository = null,
        IMemoryCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(entityRepository ?? new FakeEntityRepository());
        services.AddSingleton(trendingRepository ?? new FakeTrendingRepository());
        services.AddSingleton(articleFeedRepository ?? new FakeArticleFeedRepository());
        services.AddSingleton(cache ?? new MemoryCache(new MemoryCacheOptions()));

        var provider = services.BuildServiceProvider();

        return new AnalyticsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IMemoryCache>(),
            NullLogger<AnalyticsService>.Instance);
    }
}

internal sealed class FakeTrendingRepository : ITrendingRepository
{
    public IEnumerable<Entity> Entities { get; set; } = [];

    public IEnumerable<Entity> GetTrendingEntitiesAsync(DateOnly fromDate, DateOnly toDate, int topN = 10) =>
        Entities;
}

internal sealed class FakeEntityRepository : IEntityRepository
{
    public int BrowseCalls { get; private set; }
    public int LastPage { get; private set; }
    public int LastPageSize { get; private set; }
    public string? LastSearch { get; private set; }
    public (IEnumerable<(Entity Entity, int MentionCount)> Items, int TotalCount) BrowseResult { get; set; } = ([], 0);
    public Dictionary<string, IEnumerable<Article>> MentioningArticlesByEntity { get; } = [];
    public IEnumerable<(string Domain, int Count)> ArticleDomains { get; set; } = [];
    public int ArticleDomainCalls { get; private set; }
    public Entity? EntityById { get; set; }
    public int EntityByIdCalls { get; private set; }

    public (IEnumerable<(Entity Entity, int MentionCount)> Items, int TotalCount) GetEntitiesWithMentionCountAsync(
        DateOnly from,
        DateOnly to,
        int page,
        int pageSize,
        string? search = null)
    {
        BrowseCalls++;
        LastPage = page;
        LastPageSize = pageSize;
        LastSearch = search;
        return BrowseResult;
    }

    public IDictionary<string, IEnumerable<Article>> GetMentioningArticlesAsync(
        IEnumerable<string> entityIds,
        DateOnly from,
        DateOnly to) =>
        MentioningArticlesByEntity
            .Where(pair => entityIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    public IEnumerable<(string Domain, int Count)> GetArticleDomainsAsync(DateOnly from, DateOnly to)
    {
        ArticleDomainCalls++;
        return ArticleDomains;
    }

    public Entity? GetEntityByIdAsync(string entityId)
    {
        EntityByIdCalls++;
        return EntityById;
    }

    public IEnumerable<Entity> GetEntitiesAsync() => [];
    public IEnumerable<Article> GetMentioningArticlesAsync(string entityId, DateOnly from, DateOnly to) => [];
    public (IEnumerable<Article> Items, int TotalCount) GetMentioningArticlesPaginatedAsync(string entityId, DateOnly from, DateOnly to, int page, int pageSize) => ([], 0);
    public IEnumerable<(string Domain, int Count)> GetMentioningArticlesByDomainAsync(string entityId, DateOnly from, DateOnly to) => [];
    public IReadOnlyList<EntityCoMention> GetCoMentionedEntities(string entityId, DateOnly from, DateOnly to, int limit) => [];
    public IReadOnlyList<Agitprop.Core.Models.SourceEntityCoverage> GetSourceEntityCoverage(DateOnly from, DateOnly to, int sourceLimit, int entityLimit) => [];
    public IEnumerable<Entity> SearchEntitiesAsync(string query) => [];
    public IEnumerable<Entity> GetEntitiesPaginatedAsync(DateOnly from, DateOnly to, int page, int pageSize) => [];
    public Task<IEnumerable<Entity>> GetAllEntitiesAsync(DateOnly from, DateOnly to) => Task.FromResult<IEnumerable<Entity>>([]);
}

internal sealed class FakeArticleFeedRepository : IArticleFeedRepository
{
    public int Calls { get; private set; }
    public int LastPage { get; private set; }
    public int LastPageSize { get; private set; }

    public ArticleFeedPage GetArticleFeed(
        DateOnly from, DateOnly to, string? entityId, string? domain, string? search, int page, int pageSize)
    {
        Calls++;
        LastPage = page;
        LastPageSize = pageSize;
        return new ArticleFeedPage([], 0);
    }

    public IReadOnlyList<DailyArticleCount> GetDailyArticleCounts(DateOnly from, DateOnly to, string? entityId, string? domain)
    {
        Calls++;
        return Enumerable.Range(0, 10)
            .Select(index => new DailyArticleCount(
                new DateOnly(2026, 1, 1).AddDays(index),
                index == 9 ? 20 : 2 + index % 2))
            .ToList();
    }

    public IReadOnlyList<PublisherFirstSeen> GetPublisherFirstSeen(string entityId, DateOnly from, DateOnly to)
    {
        Calls++;
        return [];
    }
}
