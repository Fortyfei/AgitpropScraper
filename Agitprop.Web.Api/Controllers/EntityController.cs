using System.Diagnostics;
using Agitprop.Core.Interfaces;
using Agitprop.Web.Api.DTOs;
using Agitprop.Web.Api.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Agitprop.Web.Api.Controllers;

/// <summary>
/// Provides minimal endpoints for homepage entity analytics.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EntitiesController : ControllerBase
{
    private readonly ILogger<EntitiesController> _logger;
    private readonly IEntityRepository _entityRepository;
    private readonly ITrendingRepository _trendingRepository;
    private readonly IMemoryCache _cache;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.EntitiesController");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public EntitiesController(
        ILogger<EntitiesController> logger,
        IEntityRepository repository,
        ITrendingRepository trendingRepository,
        IMemoryCache cache)
    {
        _logger = logger;
        _entityRepository = repository;
        _trendingRepository = trendingRepository;
        _cache = cache;
    }

    [HttpGet]
    public ActionResult<EntityBrowseResponse> GetEntities(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null)
    {
        using var activity = _activitySource.StartActivity("GetEntities", ActivityKind.Server);

        if (from == default || to == default)
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The 'from' date must be earlier than or equal to the 'to' date." });

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 25;

        var safeSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var cacheKey = $"entities-browse:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:p{page}:ps{pageSize}:s{safeSearch}";
        if (_cache.TryGetValue(cacheKey, out EntityBrowseResponse? cached) && cached is not null)
            return Ok(cached);

        try
        {
            var (items, totalCount) = _entityRepository.GetEntitiesWithMentionCountAsync(from, to, page, pageSize, safeSearch);

            var response = new EntityBrowseResponse
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = items.Select(x => new EntityBrowseItem
                {
                    Id = x.Entity.Id ?? string.Empty,
                    Name = x.Entity.Name,
                    Type = x.Entity.Type ?? string.Empty,
                    MentionCount = x.MentionCount
                }).ToList()
            };

            _cache.Set(cacheKey, response, CacheDuration);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing entities");
            return StatusCode(500, new { error = "Failed to retrieve entities." });
        }
    }

    [HttpGet("{id}/domain-stats")]
    public ActionResult<EntityDomainStatsResponse> GetEntityDomainStats(
        string id,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        using var activity = _activitySource.StartActivity("GetEntityDomainStats", ActivityKind.Server);

        if (from == default || to == default)
            return BadRequest(new { error = "Both from and to query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The from date must be earlier than or equal to the to date." });

        var cacheKey = $"domain-stats:{id}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        if (_cache.TryGetValue(cacheKey, out EntityDomainStatsResponse? cached) && cached is not null)
            return Ok(cached);

        try
        {
            var domains = _entityRepository.GetMentioningArticlesByDomainAsync(id, from, to).ToList();
            var total = domains.Sum(d => d.Count);

            var response = new EntityDomainStatsResponse
            {
                TotalCount = total,
                Domains = domains.Select(d => new DomainStatDto
                {
                    Domain = d.Domain,
                    Count = d.Count,
                    Percent = total > 0 ? d.Count * 100.0 / total : 0
                }).ToList()
            };

            _cache.Set(cacheKey, response, CacheDuration);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving domain stats for entity {Id}", id);
            return StatusCode(500, new { error = "Failed to retrieve domain statistics." });
        }
    }

    [HttpGet("{id}")]
    public ActionResult<EntityResponse> GetEntityById(string id)
    {
        using var activity = _activitySource.StartActivity("GetEntityById", ActivityKind.Server);

        var cacheKey = $"entity:{id}";
        if (_cache.TryGetValue(cacheKey, out EntityResponse? cached) && cached is not null)
            return Ok(cached);

        try
        {
            var entity = _entityRepository.GetEntityByIdAsync(id);
            if (entity is null)
                return NotFound(new { error = $"Entity '{id}' not found." });

            var response = new EntityResponse { Id = entity.Id ?? id, Name = entity.Name, Type = entity.Type ?? string.Empty };
            _cache.Set(cacheKey, response, TimeSpan.FromHours(1));
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving entity {Id}", id);
            return StatusCode(500, new { error = "Failed to retrieve entity." });
        }
    }

    [HttpGet("{id}/articles")]
    public ActionResult<EntityArticlesResponse> GetEntityArticles(
        string id,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        using var activity = _activitySource.StartActivity("GetEntityArticles", ActivityKind.Server);

        if (from == default || to == default)
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The 'from' date must be earlier than or equal to the 'to' date." });

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var cacheKey = $"articles:{id}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:p{page}:ps{pageSize}";
        if (_cache.TryGetValue(cacheKey, out EntityArticlesResponse? cached) && cached is not null)
            return Ok(cached);

        try
        {
            var (items, totalCount) = _entityRepository.GetMentioningArticlesPaginatedAsync(id, from, to, page, pageSize);

            var response = new EntityArticlesResponse
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = items.Select(a => new ArticleDto
                {
                    Id = a.Id ?? string.Empty,
                    Title = a.Title,
                    Url = a.Url,
                    PublishedTime = a.PublishedTime
                }).ToList()
            };

            _cache.Set(cacheKey, response, CacheDuration);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving articles for entity {Id}", id);
            return StatusCode(500, new { error = "Failed to retrieve entity articles." });
        }
    }

    [HttpGet("timeline")]
    public ActionResult<EntitiesTimelineResponse> GetEntitiesTimeline(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] List<string>? entities)
    {
        using var activity = _activitySource.StartActivity("GetEntitiesTimeline", ActivityKind.Server);

        if (from == default || to == default)
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The 'from' date must be earlier than or equal to the 'to' date." });

        if (entities == null || !entities.Any())
            return BadRequest(new { error = "At least one entity id must be provided." });

        var entityIds = entities
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id)
            .ToList();

        if (!entityIds.Any())
            return BadRequest(new { error = "At least one valid entity id must be provided." });

        var cacheKey = $"timeline:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{string.Join(",", entityIds)}";
        if (_cache.TryGetValue(cacheKey, out EntitiesTimelineResponse? cached) && cached is not null)
            return Ok(cached);

        try
        {
            var mentionings = _entityRepository.GetMentioningArticlesAsync(entityIds, from, to);

            var timeline = mentionings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .GroupBy(article => DateOnly.FromDateTime(article.PublishedTime))
                    .Select(group => new EntityTimelinePoint
                    {
                        Date = group.Key,
                        Count = group.Count()
                    })
                    .OrderBy(point => point.Date)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

            var response = new EntitiesTimelineResponse { Timeline = timeline };
            _cache.Set(cacheKey, response, CacheDuration);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing entities timeline");
            return StatusCode(500, new { error = "Failed to compute entities timeline." });
        }
    }
}
