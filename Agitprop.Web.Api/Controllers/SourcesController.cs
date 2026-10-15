using System.Diagnostics;
using Agitprop.Core.Interfaces;
using Agitprop.Web.Api.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Agitprop.Web.Api.Controllers;

[ApiController]
[Route("api/sources")]
public class SourcesController : ControllerBase
{
    private readonly ILogger<SourcesController> _logger;
    private readonly IEntityRepository _entityRepository;
    private readonly IMemoryCache _cache;
    private static readonly ActivitySource ActivitySource = new("Agitprop.Web.Api.Controllers.SourcesController");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public SourcesController(
        ILogger<SourcesController> logger,
        IEntityRepository entityRepository,
        IMemoryCache cache)
    {
        _logger = logger;
        _entityRepository = entityRepository;
        _cache = cache;
    }

    [HttpGet]
    public ActionResult<SourcesResponse> GetSources(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        using var activity = ActivitySource.StartActivity("GetSources", ActivityKind.Internal);
        activity?.SetTag("api.operation", "sources.list");

        if (from == default || to == default)
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        if (from > to)
            return BadRequest(new { error = "The 'from' date must be earlier than or equal to the 'to' date." });

        var cacheKey = $"sources:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";
        if (_cache.TryGetValue(cacheKey, out SourcesResponse? cached) && cached is not null)
        {
            ApiMetrics.RecordCacheResult(activity, "sources.list", "hit");
            return Ok(cached);
        }

        ApiMetrics.RecordCacheResult(activity, "sources.list", "miss");

        try
        {
            var domains = _entityRepository.GetArticleDomainsAsync(from, to).ToList();
            var total = domains.Sum(domain => domain.Count);
            var response = new SourcesResponse
            {
                TotalCount = total,
                Sources = domains.Select(domain => new SourceStatDto
                {
                    Domain = domain.Domain,
                    Count = domain.Count,
                    Percent = total > 0 ? domain.Count * 100.0 / total : 0
                }).ToList()
            };

            _cache.Set(cacheKey, response, CacheDuration);
            activity?.SetTag("api.result_count", response.Sources.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving source statistics from {From} to {To}", from, to);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return StatusCode(500, new { error = "Failed to retrieve source statistics." });
        }
    }
}
