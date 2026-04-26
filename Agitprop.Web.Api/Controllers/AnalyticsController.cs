using System.Diagnostics;

using Agitprop.Core.Interfaces;
using Agitprop.Web.Api.DTOs.Responses;

using Microsoft.AspNetCore.Mvc;

namespace Agitprop.Web.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
    private readonly ILogger<AnalyticsController> _logger;
    private readonly IEntityRepository _entityRepository;
    private readonly ITrendingRepository _trendingRepository;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.ActivitiesController");

        public AnalyticsController(
            ILogger<AnalyticsController> logger,
            IEntityRepository repository,
            ITrendingRepository trendingRepository)
        {
            _logger = logger;
            _entityRepository = repository;
            _trendingRepository = trendingRepository;
        }
    

    [HttpGet("topmentions")]
    public ActionResult<TopMentionedEntitiesResponse> GetTopMentionedEntities(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        using var activity = _activitySource.StartActivity("GetTopMentionedEntities", ActivityKind.Server);

        if (from == default || to == default)
        {
            return BadRequest(new { error = "Both from and to query parameters are required." });
        }

        if (from > to)
        {
            return BadRequest(new { error = "The from date must be earlier than or equal to the to date." });
        }

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

            return Ok(new TopMentionedEntitiesResponse { Entities = topEntities });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing top mentioned entities");
            return StatusCode(500, new { error = "Failed to compute top mentioned entities." });
        }
    }
    }
}
