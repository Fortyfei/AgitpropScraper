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
        var from = request.StartDate == default ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)) : request.StartDate;
        var to = request.EndDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : request.EndDate;

        var entities = _entityRepository.GetEntitiesPaginatedAsync(
            from,
            to,
            request.Page,
            request.PageSize);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            entities = entities.Where(e => e.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase));
        }

        var entityList = entities.ToList();
        var entityIds = entityList
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Id))
            .Select(entity => entity.Id!)
            .ToList();
        var mentionings = _entityRepository.GetMentioningArticlesAsync(entityIds, from, to);

        var mappedEntities = entityList.Select(e => new EntityDto
        {
            Id = e.Id ?? string.Empty,
            Name = e.Name,
            Type = e.Type,
            MentionCount = !string.IsNullOrWhiteSpace(e.Id) && mentionings.TryGetValue(e.Id, out var articles)
                ? articles.Count()
                : 0
        });

        var response = new PaginatedEntitiesResponse
        {
            Entities = mappedEntities,
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
        var from = request.StartDate == default ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)) : request.StartDate;
        var to = request.EndDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : request.EndDate;
        var entity =  _entityRepository.GetEntityByIdAsync(entityId);
        if (entity == null)
            return NotFound();

        var articles = _entityRepository.GetMentioningArticlesAsync(entityId,
                                                                    from,
                                                                    to);


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
        var from = request.StartDate == default ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)) : request.StartDate;
        var to = request.EndDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : request.EndDate;
        var articles = _entityRepository.GetMentioningArticlesAsync(
            entityId,
            from,
            to);

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
        var from = request.StartDate == default ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)) : request.StartDate;
        var to = request.EndDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : request.EndDate;

        var articles = _entityRepository.GetMentioningArticlesAsync(
            entityId,
            from,
            to);

        var related = articles
            .SelectMany(article => article.MentionedEntities)
            .Where(entity => entity.Id != entityId)
            .GroupBy(entity => entity.Id)
            .Select(g => new EntityCoMentionDto
            {
                Id = g.Key,
                Name = g.First().Name,
                Type = g.First().Type,
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
