using System.Diagnostics;

using Agitprop.Core.Interfaces;
using Agitprop.Web.Models;

using Microsoft.Extensions.Caching.Memory;

namespace Agitprop.Web.Services;

/// <summary>
/// Serves the dashboard queries straight from the repositories.
/// Every call opens its own DI scope so the EF Core context lives exactly as long as the query,
/// which keeps a long-lived Blazor circuit from sharing a single context.
/// Query spans are children of the surrounding <see cref="Agitprop.Web.Diagnostics.UserJourney"/>,
/// so one user interaction is exported as one trace.
/// </summary>
public class AnalyticsService
{
    private const int MaxArticleRangeDays = 366;
    private const int DefaultBrowsePageSize = 25;
    private const int DefaultArticlePageSize = 20;
    private const int MaxPageSize = 100;
    private const string DateRangeError =
        "Both 'from' and 'to' dates are required and 'from' must not be later than 'to'.";

    private static readonly ActivitySource ActivitySource = new("Agitprop.Web.AnalyticsService");
    private static readonly TimeSpan BrowseCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DetailCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan FeedCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<AnalyticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public Task<EntityBrowsePage?> GetEntitiesBrowseAsync(
        DateOnly from,
        DateOnly to,
        int page,
        int pageSize,
        string? search)
    {
        EnsureRange(from, to);

        page = Math.Max(page, 1);
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultBrowsePageSize : pageSize;
        var safeSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        return Task.FromResult(CachedQuery(
            "entities.browse",
            $"entities-browse:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:p{page}:ps{pageSize}:s{safeSearch}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var (items, totalCount) =
                    entityRepository.GetEntitiesWithMentionCountAsync(from, to, page, pageSize, safeSearch);

                return new EntityBrowsePage
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    Items = items.Select(item => new EntityBrowseItem
                    {
                        Id = item.Entity.Id ?? string.Empty,
                        Name = item.Entity.Name,
                        Type = item.Entity.Type ?? string.Empty,
                        MentionCount = item.MentionCount
                    }).ToList()
                };
            })));
    }

    public Task<List<DomainStat>> GetEntityDomainStatsAsync(string entityId, DateOnly from, DateOnly to)
    {
        EnsureRange(from, to);

        var result = CachedQuery(
            "entities.domain_stats",
            $"domain-stats:{entityId}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var domains = entityRepository.GetMentioningArticlesByDomainAsync(entityId, from, to).ToList();
                var total = domains.Sum(domain => domain.Count);

                return domains.Select(domain => new DomainStat
                {
                    Domain = domain.Domain,
                    Count = domain.Count,
                    Percent = total > 0 ? domain.Count * 100.0 / total : 0
                }).ToList();
            }));

        return Task.FromResult(result ?? []);
    }

    public Task<List<SourceStat>?> GetSourcesAsync(DateOnly from, DateOnly to)
    {
        EnsureRange(from, to);

        return Task.FromResult(CachedQuery(
            "sources.list",
            $"sources:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var domains = entityRepository.GetArticleDomainsAsync(from, to).ToList();
                var total = domains.Sum(domain => domain.Count);

                return domains.Select(domain => new SourceStat
                {
                    Domain = domain.Domain,
                    Count = domain.Count,
                    Percent = total > 0 ? domain.Count * 100.0 / total : 0
                }).ToList();
            })));
    }

    public Task<EntityInfo?> GetEntityByIdAsync(string entityId)
    {
        return Task.FromResult(CachedQuery(
            "entities.get",
            $"entity:{entityId}",
            DetailCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var entity = entityRepository.GetEntityByIdAsync(entityId);

                return entity is null
                    ? null
                    : new EntityInfo
                    {
                        Id = entity.Id ?? entityId,
                        Name = entity.Name,
                        Type = entity.Type ?? string.Empty
                    };
            })));
    }

    public Task<ArticlePage?> GetEntityArticlesAsync(
        string entityId,
        DateOnly from,
        DateOnly to,
        int page,
        int pageSize)
    {
        EnsureRange(from, to);

        page = Math.Max(page, 1);
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultArticlePageSize : pageSize;

        return Task.FromResult(CachedQuery(
            "entities.articles",
            $"articles:{entityId}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:p{page}:ps{pageSize}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var (items, totalCount) =
                    entityRepository.GetMentioningArticlesPaginatedAsync(entityId, from, to, page, pageSize);

                return new ArticlePage
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    Items = items.Select(article => new ArticleSummary
                    {
                        Id = article.Id ?? string.Empty,
                        Title = article.Title,
                        Url = article.Url,
                        PublishedTime = article.PublishedTime
                    }).ToList()
                };
            })));
    }

    public Task<IEnumerable<EntitySummary>> GetTopMentionedEntitiesAsync(DateOnly from, DateOnly to)
    {
        EnsureRange(from, to);

        var result = CachedQuery(
            "analytics.top_mentions",
            $"topmentions:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}",
            BrowseCacheDuration,
            provider =>
            {
                var entityRepository = provider.GetRequiredService<IEntityRepository>();
                var trendingRepository = provider.GetRequiredService<ITrendingRepository>();

                var trendingEntities = trendingRepository.GetTrendingEntitiesAsync(from, to).Take(8).ToList();
                var entityIds = trendingEntities
                    .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                    .Select(entity => entity.Id!)
                    .ToList();

                var mentionings = entityRepository.GetMentioningArticlesAsync(entityIds, from, to);

                return trendingEntities
                    .Select(entity => new EntitySummary
                    {
                        Id = entity.Id ?? string.Empty,
                        Name = entity.Name,
                        Mentions =
                            !string.IsNullOrWhiteSpace(entity.Id) && mentionings.TryGetValue(entity.Id, out var articles)
                                ? articles.Count()
                                : 0
                    })
                    .OrderByDescending(entity => entity.Mentions)
                    .ThenBy(entity => entity.Name, StringComparer.Ordinal)
                    .ToList();
            });

        return Task.FromResult<IEnumerable<EntitySummary>>(result ?? []);
    }

    public Task<List<CoMentionEntity>?> GetCoMentionsAsync(
        string entityId,
        DateOnly from,
        DateOnly to,
        int limit = 8)
    {
        EnsureRange(from, to);

        if (!Guid.TryParse(entityId, out _))
        {
            throw new ArgumentException("A valid entity id is required.", nameof(entityId));
        }

        limit = Math.Clamp(limit, 1, 25);

        return Task.FromResult(CachedQuery(
            "entities.co_mentions",
            $"entity-co-mentions:{entityId}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:limit{limit}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                if (entityRepository.GetEntityByIdAsync(entityId) is null)
                {
                    return null;
                }

                return entityRepository.GetCoMentionedEntities(entityId, from, to, limit)
                    .Select(entity => new CoMentionEntity
                    {
                        EntityId = entity.EntityId,
                        EntityName = entity.EntityName,
                        EntityType = entity.EntityType,
                        SharedArticleCount = entity.SharedArticleCount
                    })
                    .ToList();
            })));
    }

    public Task<List<SourceEntityCoverage>?> GetSourceEntityCoverageAsync(
        DateOnly from,
        DateOnly to,
        int sourceLimit = 8,
        int entityLimit = 8)
    {
        EnsureRange(from, to);

        sourceLimit = Math.Clamp(sourceLimit, 1, 12);
        entityLimit = Math.Clamp(entityLimit, 1, 12);

        return Task.FromResult(CachedQuery(
            "analytics.source_entities",
            $"source-entities:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:sources{sourceLimit}:entities{entityLimit}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
                entityRepository.GetSourceEntityCoverage(from, to, sourceLimit, entityLimit)
                    .Select(source => new SourceEntityCoverage
                    {
                        Domain = source.Domain,
                        ArticleCount = source.ArticleCount,
                        Entities = source.Entities.Select(entity => new EntityCoverageStat
                        {
                            EntityId = entity.EntityId,
                            EntityName = entity.EntityName,
                            EntityType = entity.EntityType,
                            ArticleCount = entity.ArticleCount,
                            PercentOfSourceArticles = entity.PercentOfSourceArticles
                        }).ToList()
                    })
                    .ToList())));
    }

    public Task<FeedPage?> GetArticleFeedAsync(
        DateOnly from,
        DateOnly to,
        string? entityId,
        string? domain,
        string? search,
        int page,
        int pageSize)
    {
        EnsureFeedRange(from, to, entityId);

        page = Math.Max(page, 0);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var safeSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        return Task.FromResult(RunQuery(
            "articles.feed",
            provider =>
            {
                var repository = provider.GetRequiredService<IArticleFeedRepository>();
                var feed = repository.GetArticleFeed(from, to, entityId, domain, safeSearch, page, pageSize);

                return new FeedPage
                {
                    TotalCount = feed.TotalCount,
                    Items = feed.Items.Select(item => new FeedArticle
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Url = item.Url,
                        Domain = item.Domain,
                        PublishedTime = item.PublishedTime,
                        Entities = item.Entities.Select(entity => new FeedEntity
                        {
                            Id = entity.Id,
                            Name = entity.Name,
                            Type = entity.Type
                        }).ToList()
                    }).ToList()
                };
            }));
    }

    public Task<List<DailyCount>?> GetDailyArticleCountsAsync(
        DateOnly from,
        DateOnly to,
        string? entityId,
        string? domain)
    {
        EnsureFeedRange(from, to, entityId);

        return Task.FromResult(CachedQuery(
            "articles.daily",
            $"articles-daily:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{entityId}:{domain}",
            FeedCacheDuration,
            provider =>
            {
                var repository = provider.GetRequiredService<IArticleFeedRepository>();
                var days = Agitprop.Core.Models.SpikeDetector.Mark(
                    repository.GetDailyArticleCounts(from, to, entityId, domain));

                return days.Select(day => new DailyCount
                {
                    Date = day.Date,
                    Count = day.Count,
                    IsSpike = day.IsSpike
                }).ToList();
            }));
    }

    public Task<List<FirstPublisher>?> GetFirstPublishersAsync(string entityId, DateOnly from, DateOnly to)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("An entity id is required.", nameof(entityId));
        }

        EnsureFeedRange(from, to, entityId);

        return Task.FromResult(CachedQuery(
            "articles.first_publishers",
            $"articles-first-publishers:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{entityId}",
            FeedCacheDuration,
            provider =>
            {
                var repository = provider.GetRequiredService<IArticleFeedRepository>();

                return repository.GetPublisherFirstSeen(entityId, from, to)
                    .Select(item => new FirstPublisher
                    {
                        Domain = item.Domain,
                        FirstPublished = item.FirstPublished,
                        ArticleCount = item.ArticleCount
                    })
                    .ToList();
            }));
    }

    public Task<Dictionary<string, List<TimelinePoint>>> GetEntitiesTimelineAsync(
        DateOnly from,
        DateOnly to,
        IEnumerable<string> entityIds)
    {
        EnsureRange(from, to);
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = entityIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var empty = new Dictionary<string, List<TimelinePoint>>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return Task.FromResult(empty);
        }

        var result = CachedQuery(
            "entities.timeline",
            $"entities-timeline:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{string.Join(",", ids)}",
            BrowseCacheDuration,
            FromEntityRepository(entityRepository =>
            {
                var mentionings = entityRepository.GetMentioningArticlesAsync(ids, from, to);

                // Emit every day in the requested range (zero-filled) so sparse series still span the whole window.
                return mentionings.ToDictionary(
                    pair => pair.Key,
                    pair =>
                    {
                        var countsByDay = pair.Value
                            .GroupBy(article => Agitprop.Core.ReportingTimeZone.ToDay(article.PublishedTime))
                            .ToDictionary(group => group.Key, group => group.Count());

                        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                            .Select(offset => from.AddDays(offset))
                            .Select(day => new TimelinePoint
                            {
                                Date = day,
                                Count = countsByDay.GetValueOrDefault(day)
                            })
                            .ToList();
                    },
                    StringComparer.OrdinalIgnoreCase);
            }));

        return Task.FromResult(result ?? empty);
    }

    private static void EnsureRange(DateOnly from, DateOnly to)
    {
        if (from == default || to == default || from > to)
        {
            throw new ArgumentException(DateRangeError);
        }
    }

    private static void EnsureFeedRange(DateOnly from, DateOnly to, string? entityId)
    {
        EnsureRange(from, to);

        if (to.DayNumber - from.DayNumber >= MaxArticleRangeDays)
        {
            throw new ArgumentException($"The date range cannot exceed {MaxArticleRangeDays} days.");
        }

        if (!string.IsNullOrWhiteSpace(entityId) && !Guid.TryParse(entityId, out _))
        {
            throw new ArgumentException("A valid entity id is required.", nameof(entityId));
        }
    }

    /// <summary>
    /// Runs a query behind the memory cache. The span is created for cache hits as well, so a
    /// journey shows every query it issued.
    /// </summary>
    private T? CachedQuery<T>(
        string operation,
        string cacheKey,
        TimeSpan cacheDuration,
        Func<IServiceProvider, T?> query)
        where T : class
    {
        using var activity = StartActivity(operation);

        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            RecordCacheResult(activity, operation, "hit");
            return cached;
        }

        RecordCacheResult(activity, operation, "miss");

        var result = RunQuery(activity, operation, query);
        if (result is not null)
        {
            _cache.Set(cacheKey, result, cacheDuration);
        }

        return result;
    }

    private T? RunQuery<T>(string operation, Func<IServiceProvider, T?> query) where T : class
    {
        using var activity = StartActivity(operation);
        return RunQuery(activity, operation, query);
    }

    /// <summary>
    /// Opens a scope for the query and turns storage failures into a null result so the dashboards
    /// keep rendering their existing "unable to load" state instead of faulting the circuit.
    /// </summary>
    private T? RunQuery<T>(Activity? activity, string operation, Func<IServiceProvider, T?> query)
        where T : class
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return query(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard query {Operation} failed", operation);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return null;
        }
    }

    private static Func<IServiceProvider, T?> FromEntityRepository<T>(Func<IEntityRepository, T?> query)
        where T : class =>
        provider => query(provider.GetRequiredService<IEntityRepository>());

    private static Activity? StartActivity(string operation)
    {
        // Implicitly parented by Activity.Current: inside a UserJourney this keeps one trace,
        // and during prerender it nests under the inbound HTTP request span.
        var activity = ActivitySource.StartActivity(operation, ActivityKind.Internal);
        activity?.SetTag("query.operation", operation);
        return activity;
    }

    private static void RecordCacheResult(Activity? activity, string operation, string result)
    {
        activity?.SetTag("cache.result", result);
        WebMetrics.RecordCacheResult(activity, operation, result);
    }
}
