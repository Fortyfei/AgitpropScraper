using System.Diagnostics;

using Agitprop.Core.Interfaces;
using Agitprop.Web.Api.DTOs.Responses;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Agitprop.Web.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
    private readonly ILogger<AnalyticsController> _logger;
    private readonly IEntityRepository _entityRepository;
    private readonly ITrendingRepository _trendingRepository;
    private readonly IMemoryCache _cache;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.ActivitiesController");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        public AnalyticsController(
            ILogger<AnalyticsController> logger,
            IEntityRepository repository,
            ITrendingRepository trendingRepository,
            IMemoryCache cache)
        {
            _logger = logger;
            _entityRepository = repository;
            _trendingRepository = trendingRepository;
            _cache = cache;
        }
    

    [HttpGet("topmentions")]
    public ActionResult<TopMentionedEntitiesResponse> GetTopMentionedEntities(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        using var activity = _activitySource.StartActivity("GetTopMentionedEntities", ActivityKind.Internal);
        activity?.SetTag("api.operation", "analytics.top_mentions");

        if (from == default || to == default)
        {
            return BadRequest(new { error = "Both from and to query parameters are required." });
        }

        if (from > to)
        {
            return BadRequest(new { error = "The from date must be earlier than or equal to the to date." });
        }

        var cacheKey = $"topmentions:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        if (_cache.TryGetValue(cacheKey, out TopMentionedEntitiesResponse? cached) && cached is not null)
        {
            ApiMetrics.RecordCacheResult(activity, "analytics.top_mentions", "hit");
            return Ok(cached);
        }
        ApiMetrics.RecordCacheResult(activity, "analytics.top_mentions", "miss");

        try
        {
            var trendingEntities = _trendingRepository.GetTrendingEntitiesAsync(from, to)
                .Take(8)
                .ToList();

            var entityIds = trendingEntities
                .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
                .Select(entity => entity.Id!)
                .ToList();

            var mentionings = _entityRepository.GetMentioningArticlesAsync(entityIds, from, to);

            var topEntities = trendingEntities
                .Select(entity => new TopMentionedEntity
                {
                    Id = entity.Id ?? string.Empty,
                    Name = entity.Name,
                    MentionCount = !string.IsNullOrWhiteSpace(entity.Id) && mentionings.TryGetValue(entity.Id, out var articles)
                        ? articles.Count()
                        : 0
                })
                .OrderByDescending(entity => entity.MentionCount)
                .ThenBy(entity => entity.Name)
                .ToList();

            var response = new TopMentionedEntitiesResponse { Entities = topEntities };
            _cache.Set(cacheKey, response, CacheDuration);
            activity?.SetTag("api.result_count", topEntities.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing top mentioned entities");
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return StatusCode(500, new { error = "Failed to compute top mentioned entities." });
        }
    }

    [HttpGet("source-entities")]
    public ActionResult<SourceEntityCoverageResponse> GetSourceEntityCoverage(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int sourceLimit = 8,
        [FromQuery] int entityLimit = 8)
    {
        using var activity = _activitySource.StartActivity("GetSourceEntityCoverage", ActivityKind.Internal);
        activity?.SetTag("api.operation", "analytics.source_entities");

        if (from == default || to == default)
            return BadRequest(new { error = "Both from and to query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The from date must be earlier than or equal to the to date." });

        sourceLimit = Math.Clamp(sourceLimit, 1, 12);
        entityLimit = Math.Clamp(entityLimit, 1, 12);
        var cacheKey = $"source-entities:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:sources{sourceLimit}:entities{entityLimit}";
        if (_cache.TryGetValue(cacheKey, out SourceEntityCoverageResponse? cached) && cached is not null)
        {
            ApiMetrics.RecordCacheResult(activity, "analytics.source_entities", "hit");
            return Ok(cached);
        }
        ApiMetrics.RecordCacheResult(activity, "analytics.source_entities", "miss");

        try
        {
            var sources = _entityRepository.GetSourceEntityCoverage(from, to, sourceLimit, entityLimit)
                .Select(source => new SourceCoverageItem
                {
                    Domain = source.Domain,
                    ArticleCount = source.ArticleCount,
                    Entities = source.Entities.Select(entity => new SourceEntityCoverageItem
                    {
                        EntityId = entity.EntityId,
                        EntityName = entity.EntityName,
                        EntityType = entity.EntityType,
                        ArticleCount = entity.ArticleCount,
                        PercentOfSourceArticles = entity.PercentOfSourceArticles
                    }).ToList()
                })
                .ToList();

            var response = new SourceEntityCoverageResponse { Sources = sources };
            _cache.Set(cacheKey, response, CacheDuration);
            activity?.SetTag("api.result_count", sources.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing publisher-by-entity coverage");
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return StatusCode(500, new { error = "Failed to compute publisher-by-entity coverage." });
        }
    }
    }
}
