using System.Diagnostics;

using Agitprop.Core.Interfaces;

using Microsoft.AspNetCore.Mvc;

using OpenTelemetry.Trace;

namespace Agitprop.Web.Api.Controllers;

[ApiController]
[Route("api/trending")]
public class TrendsController : ControllerBase
{
    private readonly ITrendingRepository _trendingRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly ILogger<TrendsController> _logger;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.TrendsController");

    public TrendsController(ITrendingRepository trendingRepository, ILogger<TrendsController> logger, IEntityRepository entityRepository)
    {
        _trendingRepository = trendingRepository;
        _logger = logger;
        _entityRepository = entityRepository;
    }

    [HttpGet]
    public async Task<ActionResult<TrendingResponse>> GetTrending([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        using var activity = _activitySource.StartActivity("GetTrendingEntities", ActivityKind.Server);
        try
        {
            var trending = _trendingRepository.GetTrendingEntitiesAsync(from, to);
            var mentionings = _entityRepository.GetMentioningArticlesAsync(trending.Select(e => e.Id.ToString()), from, to);

            var result = trending.Select(e => new EntityDetailsDto
            {
                Id = e.Id,
                Name = e.Name,
                TotalMentions = mentionings[e.Id.ToString()].Count(),
                MentionsCountByDate = mentionings[e.Id.ToString()].GroupBy(a => DateOnly.FromDateTime(a.PublishedTime)).ToDictionary(g => g.Key, g => g.Count())
            }).OrderByDescending(e => e.TotalMentions).ToList();
            var response = new TrendingResponse
            {
                Trending = result
            };  
            activity?.SetTag("response", response);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetTrendingEntities");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return StatusCode(500, new { error = "Internal server error." });
        }
    }
}

public class TrendingResponse
{
    public required IEnumerable<EntityDetailsDto> Trending { get; set; }
}