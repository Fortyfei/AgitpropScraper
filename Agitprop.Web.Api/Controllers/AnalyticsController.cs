using System.Diagnostics;
using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Agitprop.Web.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IEntityRepository _entityRepository;
    private readonly ITrendingRepository _trendingRepository;
    private readonly ILogger<AnalyticsController> _logger;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.AnalyticsController");

    public AnalyticsController(
        IEntityRepository entityRepository,
        ITrendingRepository trendingRepository,
        ILogger<AnalyticsController> logger)
    {
        _entityRepository = entityRepository;
        _trendingRepository = trendingRepository;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardAnalyticsResponse>> GetDashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        using var activity = _activitySource.StartActivity("GetDashboardAnalytics", ActivityKind.Server);
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var trending = _trendingRepository.GetTrendingEntitiesAsync(start, end).ToList();
            var trendingEntityIds = trending
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                .Select(entity => entity.Id!)
                .ToList();

            var mentionings = _entityRepository.GetMentioningArticlesAsync(trendingEntityIds, start, end);

            var topEntities = trending.Select(entity => new AnalyticsEntitySummary
            {
                Id = entity.Id ?? string.Empty,
                Name = entity.Name,
                Type = string.IsNullOrWhiteSpace(entity.Type) ? "MISC" : entity.Type,
                MentionCount = !string.IsNullOrWhiteSpace(entity.Id) && mentionings.TryGetValue(entity.Id, out var articles)
                    ? articles.Count()
                    : 0
            })
            .OrderByDescending(entity => entity.MentionCount)
            .Take(12)
            .ToList();

            var typeDistribution = topEntities
                .GroupBy(entity => entity.Type)
                .Select(group => new TypeDistributionPoint
                {
                    Type = group.Key,
                    Count = group.Sum(entity => entity.MentionCount)
                })
                .OrderByDescending(point => point.Count)
                .ToList();

            var mentionsOverTime = mentionings
                .SelectMany(pair => pair.Value)
                .GroupBy(article => DateOnly.FromDateTime(article.PublishedTime))
                .Select(group => new TimelinePoint
                {
                    Date = group.Key,
                    Count = group.Count()
                })
                .OrderBy(point => point.Date)
                .ToList();

            return Ok(new DashboardAnalyticsResponse
            {
                From = start,
                To = end,
                TopEntities = topEntities,
                EntityTypeDistribution = typeDistribution,
                MentionsOverTime = mentionsOverTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing dashboard analytics");
            return StatusCode(500, new { error = "Failed to compute dashboard analytics." });
        }
    }

    [HttpGet("network")]
    public async Task<ActionResult<NetworkAnalyticsResponse>> GetNetwork([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        using var activity = _activitySource.StartActivity("GetNetworkAnalytics", ActivityKind.Server);
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var trending = _trendingRepository.GetTrendingEntitiesAsync(start, end).Take(25).ToList();
            var trendingEntityIds = trending
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                .Select(entity => entity.Id!)
                .ToList();

            var mentionings = _entityRepository.GetMentioningArticlesAsync(trendingEntityIds, start, end);

            var nodes = trending.Select(entity => new AnalyticsEntitySummary
            {
                Id = entity.Id ?? string.Empty,
                Name = entity.Name,
                Type = entity.Type,
                MentionCount = !string.IsNullOrWhiteSpace(entity.Id) && mentionings.TryGetValue(entity.Id, out var articles)
                    ? articles.Count()
                    : 0
            }).ToDictionary(node => node.Id);

            var articleMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mentions in mentionings.Values)
            {
                foreach (var article in mentions)
                {
                    if (string.IsNullOrWhiteSpace(article.Id))
                    {
                        continue;
                    }

                    if (!articleMap.TryGetValue(article.Id, out var ids))
                    {
                        ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        articleMap[article.Id] = ids;
                    }

                    foreach (var entity in article.MentionedEntities)
                    {
                        if (!string.IsNullOrWhiteSpace(entity.Id) && nodes.ContainsKey(entity.Id))
                        {
                            ids.Add(entity.Id);
                        }
                    }
                }
            }

            var edgeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ids in articleMap.Values)
            {
                var entityIds = ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                for (var i = 0; i < entityIds.Count; i++)
                {
                    for (var j = i + 1; j < entityIds.Count; j++)
                    {
                        var key = $"{entityIds[i]}::{entityIds[j]}";
                        edgeWeights[key] = edgeWeights.TryGetValue(key, out var weight) ? weight + 1 : 1;
                    }
                }
            }

            var edges = edgeWeights.Select(pair =>
            {
                var parts = pair.Key.Split("::", StringSplitOptions.RemoveEmptyEntries);
                return new NetworkEdgePoint
                {
                    Source = parts[0],
                    Target = parts[1],
                    Weight = pair.Value
                };
            })
            .OrderByDescending(edge => edge.Weight)
            .Take(60)
            .ToList();

            return Ok(new NetworkAnalyticsResponse
            {
                From = start,
                To = end,
                Nodes = nodes.Values.OrderByDescending(node => node.MentionCount).ToList(),
                Edges = edges
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing network analytics");
            return StatusCode(500, new { error = "Failed to compute network analytics." });
        }
    }

    [HttpGet("articles")]
    public async Task<ActionResult<ArticleAnalyticsResponse>> GetArticles([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        using var activity = _activitySource.StartActivity("GetArticleAnalytics", ActivityKind.Server);
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var trending = _trendingRepository.GetTrendingEntitiesAsync(start, end).Take(30).ToList();
            var trendingEntityIds = trending
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                .Select(entity => entity.Id!)
                .ToList();

            var mentionings = _entityRepository.GetMentioningArticlesAsync(trendingEntityIds, start, end);

            var articles = mentionings
                .SelectMany(pair => pair.Value)
                .GroupBy(article => article.Id)
                .Select(group =>
                {
                    var first = group.First();
                    return new ArticleAnalyticsItem
                    {
                        Id = first.Id ?? string.Empty,
                        Title = first.Title,
                        Url = first.Url,
                        PublishedTime = first.PublishedTime,
                        MentionedEntities = first.MentionedEntities
                            .Select(entity => new EntityReference { Id = entity.Id ?? string.Empty, Name = entity.Name, Type = entity.Type })
                            .OrderBy(entity => entity.Name)
                            .ToList()
                    };
                })
                .Where(article => !string.IsNullOrWhiteSpace(article.Id))
                .OrderByDescending(article => article.PublishedTime)
                .Take(300)
                .ToList();

            return Ok(new ArticleAnalyticsResponse
            {
                From = start,
                To = end,
                Articles = articles
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing article analytics");
            return StatusCode(500, new { error = "Failed to compute article analytics." });
        }
    }
}

public class DashboardAnalyticsResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AnalyticsEntitySummary> TopEntities { get; set; } = [];
    public List<TypeDistributionPoint> EntityTypeDistribution { get; set; } = [];
    public List<TimelinePoint> MentionsOverTime { get; set; } = [];
}

public class NetworkAnalyticsResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AnalyticsEntitySummary> Nodes { get; set; } = [];
    public List<NetworkEdgePoint> Edges { get; set; } = [];
}

public class ArticleAnalyticsResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<ArticleAnalyticsItem> Articles { get; set; } = [];
}

public class AnalyticsEntitySummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int MentionCount { get; set; }
}

public class TypeDistributionPoint
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TimelinePoint
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

public class NetworkEdgePoint
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public class ArticleAnalyticsItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedTime { get; set; }
    public List<EntityReference> MentionedEntities { get; set; } = [];
}

public class EntityReference
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
