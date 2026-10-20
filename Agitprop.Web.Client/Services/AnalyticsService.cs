using System.Globalization;
using System.Net.Http.Json;
using Agitprop.Web.Client.Models;
using Microsoft.Extensions.Logging;

namespace Agitprop.Web.Client.Services;

public class AnalyticsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(HttpClient httpClient, ILogger<AnalyticsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<EntityBrowsePage?> GetEntitiesBrowseAsync(DateOnly from, DateOnly to, int page, int pageSize, string? search)
    {
        var query = $"api/entities?from={ToQueryDate(from)}&to={ToQueryDate(to)}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            query += $"&search={Uri.EscapeDataString(search)}";

        var response = await TryGetAsync<EntityBrowseResponseDto>(query);
        if (response is null) return null;

        return new EntityBrowsePage
        {
            TotalCount = response.TotalCount,
            Page = response.Page,
            PageSize = response.PageSize,
            Items = response.Items.Select(i => new EntityBrowseItem
            {
                Id = i.Id,
                Name = i.Name,
                Type = i.Type,
                MentionCount = i.MentionCount
            }).ToList()
        };
    }

    public async Task<List<DomainStat>> GetEntityDomainStatsAsync(string entityId, DateOnly from, DateOnly to)
    {
        var endpoint = $"api/entities/{Uri.EscapeDataString(entityId)}/domain-stats?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<EntityDomainStatsResponseDto>(endpoint);
        return response?.Domains.Select(d => new DomainStat
        {
            Domain = d.Domain,
            Count = d.Count,
            Percent = d.Percent
        }).ToList() ?? [];
    }

    public async Task<List<SourceStat>?> GetSourcesAsync(DateOnly from, DateOnly to)
    {
        var endpoint = $"api/sources?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<SourcesResponseDto>(endpoint);
        return response?.Sources.Select(source => new SourceStat
        {
            Domain = source.Domain,
            Count = source.Count,
            Percent = source.Percent
        }).ToList();
    }

    public async Task<EntityInfo?> GetEntityByIdAsync(string entityId)
    {
        var endpoint = $"api/entities/{Uri.EscapeDataString(entityId)}";
        var response = await TryGetAsync<EntityDetailDto>(endpoint);
        if (response is null) return null;
        return new EntityInfo { Id = response.Id, Name = response.Name, Type = response.Type };
    }

    public async Task<ArticlePage?> GetEntityArticlesAsync(string entityId, DateOnly from, DateOnly to, int page, int pageSize)
    {
        var endpoint = $"api/entities/{Uri.EscapeDataString(entityId)}/articles?from={ToQueryDate(from)}&to={ToQueryDate(to)}&page={page}&pageSize={pageSize}";
        var response = await TryGetAsync<EntityArticlesResponseDto>(endpoint);
        if (response is null) return null;
        return new ArticlePage
        {
            TotalCount = response.TotalCount,
            Page = response.Page,
            PageSize = response.PageSize,
            Items = response.Items.Select(a => new ArticleSummary
            {
                Id = a.Id,
                Title = a.Title,
                Url = a.Url,
                PublishedTime = a.PublishedTime
            }).ToList()
        };
    }

    public async Task<IEnumerable<EntitySummary>> GetTopMentionedEntitiesAsync(DateOnly from, DateOnly to)
    {
        var endpoint = $"api/analytics/topmentions?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<TopMentionedEntitiesResponseDto>(endpoint);

        return response?.Entities.Select(MapTopMentionedEntity) ?? Enumerable.Empty<EntitySummary>();
    }

    public async Task<List<CoMentionEntity>?> GetCoMentionsAsync(
        string entityId, DateOnly from, DateOnly to)
    {
        var endpoint = $"api/entities/{Uri.EscapeDataString(entityId)}/co-mentions?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<CoMentionsResponseDto>(endpoint);
        return response?.Entities.Select(entity => new CoMentionEntity
        {
            EntityId = entity.EntityId,
            EntityName = entity.EntityName,
            EntityType = entity.EntityType,
            SharedArticleCount = entity.SharedArticleCount
        }).ToList();
    }

    public async Task<List<SourceEntityCoverage>?> GetSourceEntityCoverageAsync(DateOnly from, DateOnly to)
    {
        var endpoint = $"api/analytics/source-entities?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<SourceEntityCoverageResponseDto>(endpoint);
        return response?.Sources.Select(source => new SourceEntityCoverage
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
        }).ToList();
    }

    public async Task<FeedPage?> GetArticleFeedAsync(
        DateOnly from, DateOnly to, string? entityId, string? domain, string? search, int page, int pageSize)
    {
        var endpoint = $"api/articles/feed?from={ToQueryDate(from)}&to={ToQueryDate(to)}&page={page}&pageSize={pageSize}"
            + FilterQuery(entityId, domain)
            + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        var response = await TryGetAsync<ArticleFeedResponseDto>(endpoint);
        if (response is null) return null;

        return new FeedPage
        {
            TotalCount = response.TotalCount,
            Items = response.Items.Select(item => new FeedArticle
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
    }

    public async Task<List<DailyCount>?> GetDailyArticleCountsAsync(
        DateOnly from, DateOnly to, string? entityId, string? domain)
    {
        var endpoint = $"api/articles/daily?from={ToQueryDate(from)}&to={ToQueryDate(to)}" + FilterQuery(entityId, domain);
        var response = await TryGetAsync<DailyCountsResponseDto>(endpoint);
        return response?.Days.Select(day => new DailyCount
        {
            Date = day.Date,
            Count = day.Count,
            IsSpike = day.IsSpike
        }).ToList();
    }

    public async Task<List<FirstPublisher>?> GetFirstPublishersAsync(string entityId, DateOnly from, DateOnly to)
    {
        var endpoint = $"api/articles/first-publishers?from={ToQueryDate(from)}&to={ToQueryDate(to)}&entityId={Uri.EscapeDataString(entityId)}";
        var response = await TryGetAsync<FirstPublishersResponseDto>(endpoint);
        return response?.Publishers.Select(publisher => new FirstPublisher
        {
            Domain = publisher.Domain,
            FirstPublished = publisher.FirstPublished,
            ArticleCount = publisher.ArticleCount
        }).ToList();
    }

    private static string FilterQuery(string? entityId, string? domain)
    {
        var query = string.Empty;
        if (!string.IsNullOrWhiteSpace(entityId))
            query += $"&entityId={Uri.EscapeDataString(entityId)}";
        if (!string.IsNullOrWhiteSpace(domain))
            query += $"&domain={Uri.EscapeDataString(domain)}";
        return query;
    }

    public async Task<Dictionary<string, List<TimelinePoint>>> GetEntitiesTimelineAsync(DateOnly from, DateOnly to, IEnumerable<string> entityIds)
    {
        var normalizedEntityIds = entityIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedEntityIds.Length == 0)
        {
            return new Dictionary<string, List<TimelinePoint>>(StringComparer.OrdinalIgnoreCase);
        }

        var query = string.Join("&", normalizedEntityIds.Select(id => $"entities={Uri.EscapeDataString(id)}"));
        var endpoint = $"api/entities/timeline?from={ToQueryDate(from)}&to={ToQueryDate(to)}&{query}";
        var response = await TryGetAsync<EntitiesTimelineResponseDto>(endpoint);

        return response?.Timeline?.ToDictionary(
                   kvp => kvp.Key,
                   kvp => kvp.Value.Select(MapTimelinePoint).ToList())
               ?? new Dictionary<string, List<TimelinePoint>>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<T?> TryGetAsync<T>(string endpoint) where T : class
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<T>(endpoint);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch analytics endpoint {Endpoint}.", endpoint);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Timed out while fetching analytics endpoint {Endpoint}.", endpoint);
            return null;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Unsupported response content type for endpoint {Endpoint}.", endpoint);
            return null;
        }
    }

    private static EntitySummary MapTopMentionedEntity(TopMentionedEntityDto entity)
    {
        return new EntitySummary
        {
            Id = entity.Id,
            Name = entity.Name,
            Mentions = entity.MentionCount
        };
    }

    private static TimelinePoint MapTimelinePoint(TimelinePointDto point)
    {
        return new TimelinePoint
        {
            Date = point.Date,
            Count = point.Count
        };
    }

    private static string ToQueryDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class EntityBrowseResponseDto
    {
        public List<EntityBrowseItemDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class EntityBrowseItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int MentionCount { get; set; }
    }

    private sealed class EntityDomainStatsResponseDto
    {
        public List<DomainStatItemDto> Domains { get; set; } = [];
        public int TotalCount { get; set; }
    }

    private sealed class SourcesResponseDto
    {
        public List<SourceStatItemDto> Sources { get; set; } = [];
        public int TotalCount { get; set; }
    }

    private sealed class SourceStatItemDto
    {
        public string Domain { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percent { get; set; }
    }

    private sealed class DomainStatItemDto
    {
        public string Domain { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percent { get; set; }
    }

    private sealed class EntityDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class EntityArticlesResponseDto
    {
        public List<ArticleItemDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class ArticleItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime PublishedTime { get; set; }
    }

    private sealed class TopMentionedEntitiesResponseDto
    {
        public List<TopMentionedEntityDto> Entities { get; set; } = [];
    }

    private sealed class TopMentionedEntityDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int MentionCount { get; set; }
    }

    private sealed class CoMentionsResponseDto
    {
        public List<CoMentionEntityDto> Entities { get; set; } = [];
    }

    private sealed class CoMentionEntityDto
    {
        public string EntityId { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int SharedArticleCount { get; set; }
    }

    private sealed class SourceEntityCoverageResponseDto
    {
        public List<SourceEntityCoverageDto> Sources { get; set; } = [];
    }

    private sealed class SourceEntityCoverageDto
    {
        public string Domain { get; set; } = string.Empty;
        public int ArticleCount { get; set; }
        public List<EntityCoverageStatDto> Entities { get; set; } = [];
    }

    private sealed class EntityCoverageStatDto
    {
        public string EntityId { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int ArticleCount { get; set; }
        public double PercentOfSourceArticles { get; set; }
    }

    private sealed class ArticleFeedResponseDto
    {
        public List<ArticleFeedItemDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
    }

    private sealed class ArticleFeedItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public DateTime PublishedTime { get; set; }
        public List<FeedEntityDto> Entities { get; set; } = [];
    }

    private sealed class FeedEntityDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class DailyCountsResponseDto
    {
        public List<DailyCountDto> Days { get; set; } = [];
    }

    private sealed class DailyCountDto
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
        public bool IsSpike { get; set; }
    }

    private sealed class FirstPublishersResponseDto
    {
        public List<FirstPublisherDto> Publishers { get; set; } = [];
    }

    private sealed class FirstPublisherDto
    {
        public string Domain { get; set; } = string.Empty;
        public DateTime FirstPublished { get; set; }
        public int ArticleCount { get; set; }
    }

    private sealed class EntitiesTimelineResponseDto
    {
        public Dictionary<string, List<TimelinePointDto>> Timeline { get; set; } = new();
    }

    private sealed class TimelinePointDto
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
    }
}
