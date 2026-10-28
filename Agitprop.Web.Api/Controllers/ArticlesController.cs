using System.Diagnostics;

using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Agitprop.Web.Api.DTOs.Responses;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Agitprop.Web.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ArticlesController : ControllerBase
{
    private const int MaxRangeDays = 366;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.ArticlesController");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ILogger<ArticlesController> _logger;
    private readonly IArticleFeedRepository _repository;
    private readonly IMemoryCache _cache;

    public ArticlesController(
        ILogger<ArticlesController> logger,
        IArticleFeedRepository repository,
        IMemoryCache cache)
    {
        _logger = logger;
        _repository = repository;
        _cache = cache;
    }

    [HttpGet("feed")]
    public ActionResult<ArticleFeedResponse> GetFeed(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? entityId,
        [FromQuery] string? domain,
        [FromQuery] string? search,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25)
    {
        using var activity = _activitySource.StartActivity("GetArticleFeed", ActivityKind.Internal);
        activity?.SetTag("api.operation", "articles.feed");

        if (ValidateRange(from, to, entityId) is { } invalid)
            return invalid;

        page = Math.Max(page, 0);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        try
        {
            var result = _repository.GetArticleFeed(from, to, entityId, domain, search, page, pageSize);
            var response = new ArticleFeedResponse
            {
                TotalCount = result.TotalCount,
                Page = page,
                PageSize = pageSize,
                Items = result.Items.Select(item => new ArticleFeedItemResponse
                {
                    Id = item.Id,
                    Title = item.Title,
                    Url = item.Url,
                    Domain = item.Domain,
                    PublishedTime = item.PublishedTime,
                    Entities = item.Entities
                        .Select(entity => new FeedEntityItem { Id = entity.Id, Name = entity.Name, Type = entity.Type })
                        .ToList()
                }).ToList()
            };

            activity?.SetTag("api.result_count", response.Items.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving article feed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return StatusCode(500, new { error = "Failed to retrieve the article feed." });
        }
    }

    [HttpGet("daily")]
    public ActionResult<DailyCountsResponse> GetDailyCounts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? entityId,
        [FromQuery] string? domain)
    {
        using var activity = _activitySource.StartActivity("GetDailyArticleCounts", ActivityKind.Internal);
        activity?.SetTag("api.operation", "articles.daily");

        if (ValidateRange(from, to, entityId) is { } invalid)
            return invalid;

        var cacheKey = $"articles-daily:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{entityId}:{domain}";
        if (_cache.TryGetValue(cacheKey, out DailyCountsResponse? cached) && cached is not null)
        {
            ApiMetrics.RecordCacheResult(activity, "articles.daily", "hit");
            return Ok(cached);
        }
        ApiMetrics.RecordCacheResult(activity, "articles.daily", "miss");

        try
        {
            var days = SpikeDetector.Mark(_repository.GetDailyArticleCounts(from, to, entityId, domain));
            var response = new DailyCountsResponse
            {
                Days = days.Select(day => new DailyCountItem { Date = day.Date, Count = day.Count, IsSpike = day.IsSpike }).ToList()
            };

            _cache.Set(cacheKey, response, CacheDuration);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving daily article counts");
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return StatusCode(500, new { error = "Failed to retrieve daily article counts." });
        }
    }

    private BadRequestObjectResult? ValidateRange(DateOnly from, DateOnly to, string? entityId)
    {
        if (from == default || to == default)
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The 'from' date must be earlier than or equal to the 'to' date." });

        if (to.DayNumber - from.DayNumber >= MaxRangeDays)
            return BadRequest(new { error = $"The date range cannot exceed {MaxRangeDays} days." });

        if (!string.IsNullOrWhiteSpace(entityId) && !Guid.TryParse(entityId, out _))
            return BadRequest(new { error = "A valid entity id is required." });

        return null;
    }
}
