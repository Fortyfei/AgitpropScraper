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

    public async Task<IEnumerable<EntitySummary>> GetTopEntitiesAsync()
    {
        var dashboard = await GetDashboardResponseAsync();
        return dashboard.TopEntities.Select(MapEntitySummary);
    }

    public async Task<IEnumerable<EntityTypeDistributionPoint>> GetEntityTypeDistributionAsync()
    {
        var dashboard = await GetDashboardResponseAsync();
        return dashboard.EntityTypeDistribution.Select(point => new EntityTypeDistributionPoint
        {
            Type = point.Type,
            Count = point.Count
        });
    }

    public async Task<IEnumerable<TimelinePoint>> GetMentionsOverTimeAsync()
    {
        var dashboard = await GetDashboardResponseAsync();
        return dashboard.MentionsOverTime.Select(MapTimelinePoint);
    }

    public async Task<IEnumerable<EntitySummary>> GetEntitiesAsync(int page = 1, int pageSize = 100)
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/entities?startDate={ToQueryDate(from)}&endDate={ToQueryDate(to)}&page={page}&pageSize={pageSize}";
        var response = await TryGetAsync<PaginatedEntitiesResponseDto>(endpoint);

        return response?.Entities.Select(entity => new EntitySummary
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            Mentions = entity.MentionCount
        }) ?? Enumerable.Empty<EntitySummary>();
    }

    public async Task<EntityDetailSummary> GetEntityDetailsAsync(string entityId)
    {
        var (from, to) = GetDefaultDateRange();
        var detailsEndpoint = $"api/entities/{entityId}/details?startDate={ToQueryDate(from)}&endDate={ToQueryDate(to)}";
        var timelineEndpoint = $"api/entities/{entityId}/timeline?startDate={ToQueryDate(from)}&endDate={ToQueryDate(to)}";

        var detailsTask = TryGetAsync<EntityDetailsResponseDto>(detailsEndpoint);
        var timelineTask = TryGetAsync<EntityTimelineResponseDto>(timelineEndpoint);
        await Task.WhenAll(detailsTask, timelineTask);

        var details = await detailsTask;
        var timeline = await timelineTask;

        return new EntityDetailSummary
        {
            Id = details?.EntityId ?? entityId,
            Name = details?.Name ?? "Unknown",
            Type = details?.Type ?? "MISC",
            TotalMentions = details?.TotalMentions ?? 0,
            Trend = timeline?.Timeline.Select(MapTimelinePoint).ToList() ?? []
        };
    }

    public async Task<IEnumerable<RelatedEntity>> GetRelatedEntitiesAsync(string entityId)
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/entities/{entityId}/related?startDate={ToQueryDate(from)}&endDate={ToQueryDate(to)}";
        var response = await TryGetAsync<RelatedEntitiesResponseDto>(endpoint);

        return response?.CoMentionedEntities.Select(entity => new RelatedEntity
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            CoOccurrence = entity.CoMentionCount
        }) ?? Enumerable.Empty<RelatedEntity>();
    }

    public async Task<IEnumerable<ArticleSummary>> GetMentioningArticlesAsync(string entityId)
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/entities/{entityId}/articles?startDate={ToQueryDate(from)}&endDate={ToQueryDate(to)}";
        var response = await TryGetAsync<EntityArticlesResponseDto>(endpoint);

        return response?.Articles.Select(article => new ArticleSummary
        {
            Id = article.Id,
            Title = article.Title,
            Url = article.ArticleUrl,
            PublishedDate = article.ArticlePublishedTime,
            MentionedEntities = []
        }) ?? Enumerable.Empty<ArticleSummary>();
    }

    public async Task<IEnumerable<NetworkNode>> GetNetworkNodesAsync()
    {
        var network = await GetNetworkResponseAsync();
        return network.Nodes.Select(node => new NetworkNode
        {
            Id = node.Id,
            Label = node.Name,
            Type = node.Type,
            Mentions = node.MentionCount
        });
    }

    public async Task<IEnumerable<NetworkEdge>> GetNetworkEdgesAsync()
    {
        var network = await GetNetworkResponseAsync();
        return network.Edges.Select(edge => new NetworkEdge
        {
            Source = edge.Source,
            Target = edge.Target,
            Weight = edge.Weight
        });
    }

    public async Task<IEnumerable<EntityTrendSeries>> GetTrendSeriesAsync()
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/trending?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<TrendingResponseDto>(endpoint);

        return response?.Trending.Select((entity, index) => new EntityTrendSeries
        {
            EntityId = entity.Id,
            Name = entity.Name,
            Color = TrendColor(index),
            Timeline = entity.MentionsCountByDate
                .OrderBy(pair => pair.Key)
                .Select(pair => new TimelinePoint
                {
                    Date = pair.Key,
                    Count = pair.Value
                })
                .ToList()
        }) ?? Enumerable.Empty<EntityTrendSeries>();
    }

    public async Task<IEnumerable<ArticleSummary>> GetArticlesAsync()
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/analytics/articles?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<ArticleAnalyticsResponseDto>(endpoint);

        return response?.Articles.Select(article => new ArticleSummary
        {
            Id = article.Id,
            Title = article.Title,
            Url = article.Url,
            PublishedDate = article.PublishedTime,
            MentionedEntities = article.MentionedEntities.Select(entity => new EntityLink
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type
            }).ToList()
        }) ?? Enumerable.Empty<ArticleSummary>();
    }

    private async Task<DashboardAnalyticsResponseDto> GetDashboardResponseAsync()
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/analytics/dashboard?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        return await TryGetAsync<DashboardAnalyticsResponseDto>(endpoint) ?? new DashboardAnalyticsResponseDto();
    }

    private async Task<NetworkAnalyticsResponseDto> GetNetworkResponseAsync()
    {
        var (from, to) = GetDefaultDateRange();
        var endpoint = $"api/analytics/network?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        return await TryGetAsync<NetworkAnalyticsResponseDto>(endpoint) ?? new NetworkAnalyticsResponseDto();
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

    private static EntitySummary MapEntitySummary(AnalyticsEntitySummaryDto entity)
    {
        return new EntitySummary
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
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

    private static (DateOnly From, DateOnly To) GetDefaultDateRange()
    {
        return (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static string ToQueryDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string TrendColor(int index)
    {
        var palette = new[] { "#2563eb", "#16a34a", "#c2410c", "#7c3aed", "#0891b2", "#be123c" };
        return palette[index % palette.Length];
    }

    private sealed class DashboardAnalyticsResponseDto
    {
        public List<AnalyticsEntitySummaryDto> TopEntities { get; set; } = [];
        public List<TypeDistributionPointDto> EntityTypeDistribution { get; set; } = [];
        public List<TimelinePointDto> MentionsOverTime { get; set; } = [];
    }

    private sealed class NetworkAnalyticsResponseDto
    {
        public List<AnalyticsEntitySummaryDto> Nodes { get; set; } = [];
        public List<NetworkEdgeDto> Edges { get; set; } = [];
    }

    private sealed class ArticleAnalyticsResponseDto
    {
        public List<ArticleAnalyticsItemDto> Articles { get; set; } = [];
    }

    private sealed class AnalyticsEntitySummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int MentionCount { get; set; }
    }

    private sealed class TypeDistributionPointDto
    {
        public string Type { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class TimelinePointDto
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
    }

    private sealed class NetworkEdgeDto
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public int Weight { get; set; }
    }

    private sealed class ArticleAnalyticsItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime PublishedTime { get; set; }
        public List<EntityReferenceDto> MentionedEntities { get; set; } = [];
    }

    private sealed class EntityReferenceDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    private sealed class PaginatedEntitiesResponseDto
    {
        public List<EntityDto> Entities { get; set; } = [];
    }

    private sealed class EntityDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int MentionCount { get; set; }
    }

    private sealed class EntityDetailsResponseDto
    {
        public string EntityId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int TotalMentions { get; set; }
    }

    private sealed class EntityTimelineResponseDto
    {
        public string EntityId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<TimelinePointDto> Timeline { get; set; } = [];
    }

    private sealed class RelatedEntitiesResponseDto
    {
        public List<RelatedEntityDto> CoMentionedEntities { get; set; } = [];
    }

    private sealed class RelatedEntityDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int CoMentionCount { get; set; }
    }

    private sealed class EntityArticlesResponseDto
    {
        public List<ArticleDto> Articles { get; set; } = [];
    }

    private sealed class ArticleDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ArticleUrl { get; set; } = string.Empty;
        public DateTime ArticlePublishedTime { get; set; }
    }

    private sealed class TrendingResponseDto
    {
        public List<TrendingEntityDto> Trending { get; set; } = [];
    }

    private sealed class TrendingEntityDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int TotalMentions { get; set; }
        public Dictionary<DateOnly, int> MentionsCountByDate { get; set; } = [];
    }
}
