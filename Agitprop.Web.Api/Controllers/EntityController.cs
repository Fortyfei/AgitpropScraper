using Microsoft.AspNetCore.Mvc;
using Agitprop.Core.Interfaces;
using Agitprop.Web.Api.DTOs.Requests;
using Agitprop.Web.Api;
using Agitprop.Web.Api.DTOs.Responses;
using Agitprop.Web.Api.DTOs;
using Agitprop.Web.Api.Models;
using System.Diagnostics;
using Agitprop.Api.Controllers;

namespace Agitprop.Web.Api.Controllers;

/// <summary>
/// Provides endpoints for browsing and analyzing entities.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EntitiesController : ControllerBase
{
    private readonly ILogger<EntitiesController> _logger;
    private readonly IEntityRepository _entityRepository;
    private static readonly ActivitySource _activitySource = new("Agitprop.Web.Api.Controllers.EntitiesController");

    public EntitiesController(
        ILogger<EntitiesController> logger,
        IEntityRepository repository)
    {
        _logger = logger;
        _entityRepository = repository;
    }

    /// <summary>
    /// Returns a paginated list of entities mentioned in articles within the given date range.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedEntitiesResponse>> GetEntitiesPaginatedAsync(
        [FromQuery] EntitiesPaginatedRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetEntitiesPaginated", ActivityKind.Server);
        var entities = _entityRepository.GetEntitiesPaginatedAsync(
            request.StartDate,
            request.EndDate,
            request.Page,
            request.PageSize);

        var response = new PaginatedEntitiesResponse
        {
            Entities = entities.ToEntityDtos(),
            Page = request.Page
        };
        activity?.SetTag("response", response);
        return Ok(response);
    }

    /// <summary>
    /// Returns details for a specific entity.
    /// </summary>
    [HttpGet("{entityId}/details")]
    public async Task<ActionResult<EntityDetailsResponse>> GetEntityDetailsAsync(
        string entityId,
        [FromQuery] EntityDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetEntityDetails", ActivityKind.Server);
        var entity = _entityRepository.GetEntityByIdAsync(entityId);

        if (entity == null)
            return NotFound();

        var articles = _entityRepository.GetMentioningArticlesAsync(entityId, request.StartDate, request.EndDate);
        var totalMentions = articles.Count();
        
        var response = new EntityDetailsResponse
        {
            EntityId = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            TotalMentions = totalMentions
        };

        activity?.SetTag("response", response);
        return Ok(response);
    }

    /// <summary>
    /// Returns a timeline of mentions for a specific entity.
    /// </summary>
    [HttpGet("{entityId}/timeline")]
    public async Task<ActionResult<EntityTimelineResponse>> GetEntityTimelineAsync(
        string entityId,
        [FromQuery] EntityTimelineRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetEntityTimeline", ActivityKind.Server);
        var entity =  _entityRepository.GetEntityByIdAsync(entityId);
        var articles = _entityRepository.GetMentioningArticlesAsync(entityId,
                                                                          request.StartDate,
                                                                          request.EndDate);


        var timeline = articles
            .GroupBy(a => DateOnly.FromDateTime(a.PublishedTime))
            .Select(g => new EntityTimelinePoint
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(p => p.Date);

        var response = new EntityTimelineResponse
        {
            EntityId = entity.Id,
            Name = entity.Name,
            Timeline = timeline.ToList()
        };

        activity?.SetTag("response", response);
        return Ok(response);
    }

    /// <summary>
    /// Returns articles that mention a specific entity.
    /// </summary>
    [HttpGet("{entityId}/articles")]
    public async Task<ActionResult<MentioningArticlesResponse>> GetArticlesMentioningEntityAsync(
        string entityId,
        [FromQuery] MentioningArticlesRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetArticlesMentioningEntity", ActivityKind.Server);
        var articles = _entityRepository.GetMentioningArticlesAsync(
            entityId,
            request.StartDate,
            request.EndDate);

        var response = new MentioningArticlesResponse { Articles = [.. articles.ToArticleDto()] };
        activity?.SetTag("response", response);

        return Ok(response);
    }

    /// <summary>
    /// Returns related entities that co-occur with the given entity.
    /// </summary>
    [HttpGet("{entityId}/related")]
    public async Task<ActionResult<RelatedEntityResponse>> GetRelatedEntitiesAsync(
        string entityId,
        [FromQuery] RelatedEntitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetRelatedEntities", ActivityKind.Server);

        // This assumes your repository will have a CoMention query later
        var articles = _entityRepository.GetMentioningArticlesAsync(
            entityId,
            request.StartDate,
            request.EndDate);

        var related = articles
            .Where(entity => entity.Id != entityId)
            .SelectMany(entity => entity.MentionedEntities)
            .GroupBy(entity => entity.Id)
            .Select(g => new EntityCoMentionDto
            {
                Id = g.Key,
                Name = g.First().Name,
                CoMentionCount = g.Count()
            })
            .OrderByDescending(r => r.CoMentionCount);

        var response = new RelatedEntityResponse
        {
            EntityId = entityId,
            CoMentionedEntities = related.ToList()
        };
        activity?.SetTag("response", response);
        return Ok(response);
    }

    /// <summary>
    /// Returns all entities for autocomplete suggestions.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<IEnumerable<EntityDto>>> GetAllEntitiesAsync(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("GetAllEntities", ActivityKind.Server);
        var entities = await _entityRepository.GetAllEntitiesAsync(startDate, endDate);
        
        var response = entities.ToEntityDtos();
        activity?.SetTag("response", response);
        return Ok(response);
    }
}
