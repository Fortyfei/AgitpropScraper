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

    public async Task<IEnumerable<EntitySummary>> GetTopMentionedEntitiesAsync(DateOnly from, DateOnly to)
    {
        var endpoint = $"api/entities/topmentioned?from={ToQueryDate(from)}&to={ToQueryDate(to)}";
        var response = await TryGetAsync<TopMentionedEntitiesResponseDto>(endpoint);

        return response?.Entities.Select(MapTopMentionedEntity) ?? Enumerable.Empty<EntitySummary>();
    }

    public async Task<Dictionary<string, List<TimelinePoint>>> GetEntitiesTimelineAsync(DateOnly from, DateOnly to, IEnumerable<string> entityIds)
    {
        var query = string.Join("&", entityIds.Select(id => $"entities={Uri.EscapeDataString(id)}"));
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
