using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;

namespace Agitprop.Sinks.Newsfeed.Database;

public class EntityRepository : IEntityRepository
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<EntityRepository> _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.Repository.EntityRepository");

    public EntityRepository(
        AppDbContext dbContext,
        ILogger<EntityRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IEnumerable<Entity> GetEntitiesAsync()
    {
        using var trace = _activitySource.StartActivity("GetEntities", ActivityKind.Internal);
        try
        {
            var results = _dbContext.Entities;
            return results.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all entities");
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IEnumerable<Entity> GetEntitiesPaginatedAsync(DateOnly startDate, DateOnly endDate, int page, int pageSize)
    {
        using var trace = _activitySource.StartActivity("GetEntitiesPaginated", ActivityKind.Internal);
        trace?.SetTag("startDate", startDate.ToString());
        trace?.SetTag("endDate", endDate.ToString());
        trace?.SetTag("page", page);
        trace?.SetTag("pageSize", pageSize);

        try
        {
            var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

            var entities = _dbContext.Entities
                .Where(e => e.Mentions.Any(m =>
                    m.Article.PublishedTime >= from &&
                    m.Article.PublishedTime <= to))
                .Skip(page * pageSize)
                .Take(pageSize);

            trace?.SetTag("resultCount", entities.Count());
            return entities.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated entities mentioned in articles between {startDate} and {endDate}", startDate, endDate);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public Entity? GetEntityByIdAsync(string entityId)
    {
        using var trace = _activitySource.StartActivity("GetEntityById", ActivityKind.Internal);
        trace?.SetTag("entityId", entityId);

        try
        {
            var results = _dbContext.Entities.Include(e => e.Mentions).AsNoTracking().FirstOrDefault(e => e.Id == Guid.Parse(entityId));
            return results?.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve entity by id {entityId}", entityId);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IEnumerable<(string Domain, int Count)> GetMentioningArticlesByDomainAsync(string entityId, DateOnly startDate, DateOnly endDate)
    {
        using var trace = _activitySource.StartActivity("GetMentioningArticlesByDomain", ActivityKind.Internal);
        trace?.SetTag("entityId", entityId);

        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        try
        {
            var uuid = Guid.Parse(entityId);

            var urls = _dbContext.Mentions
                .Where(m => m.EntityId == uuid)
                .Include(m => m.Article)
                .Where(m => m.Article.PublishedTime >= from && m.Article.PublishedTime <= to)
                .Select(m => m.Article.Url)
                .AsNoTracking()
                .AsEnumerable();

            return urls
                .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
                    : url)
                .GroupBy(domain => domain, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Domain: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve domain breakdown for entity {entityId}", entityId);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public (IEnumerable<Article> Items, int TotalCount) GetMentioningArticlesPaginatedAsync(string entityId, DateOnly startDate, DateOnly endDate, int page, int pageSize)
    {
        using var trace = _activitySource.StartActivity("GetMentioningArticlesPaginated", ActivityKind.Internal);
        trace?.SetTag("entityId", entityId);
        trace?.SetTag("page", page);
        trace?.SetTag("pageSize", pageSize);

        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        try
        {
            var uuid = Guid.Parse(entityId);

            var query = _dbContext.Mentions
                .Where(m => m.EntityId == uuid)
                .Include(m => m.Article)
                .Where(m => m.Article.PublishedTime >= from && m.Article.PublishedTime <= to)
                .Select(m => m.Article)
                .OrderByDescending(a => a.PublishedTime)
                .AsNoTracking();

            var totalCount = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToCoreModel();

            trace?.SetTag("totalCount", totalCount);
            return (items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated mentioning articles for entity {entityId}", entityId);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IEnumerable<Article> GetMentioningArticlesAsync(string entityId, DateOnly startDate, DateOnly endDate)
    {
        using var trace = _activitySource.StartActivity("GetMentioningArticles", ActivityKind.Internal);
        trace?.SetTag("entityId", entityId);

        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        trace?.SetTag("from", from.ToString("o"));
        trace?.SetTag("to", to.ToString("o"));

        try
        {
            var uuid = Guid.Parse(entityId);

            var result = _dbContext.Mentions
               .Where(a => a.EntityId == uuid)
               .Include(m => m.Article)
               .Where(a => a.Article.PublishedTime >= from && a.Article.PublishedTime <= to)
               .Select(m => m.Article)
               .OrderByDescending(a => a.PublishedTime)
               .AsNoTracking();

            return result.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve mentioning articles for entity {entityId}", entityId);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IDictionary<string,IEnumerable<Article>> GetMentioningArticlesAsync(IEnumerable<string> entityIds, DateOnly startDate, DateOnly endDate)
    {
        using var trace = _activitySource.StartActivity("GetMentioningArticles", ActivityKind.Internal);
        trace?.SetTag("entityId", entityIds);

        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        trace?.SetTag("from", from.ToString("o"));
        trace?.SetTag("to", to.ToString("o"));

        try
        {
            var result = _dbContext.Mentions
               .Where(m => entityIds.Contains(m.EntityId.ToString()))
               .Include(m => m.Article)
               .Where(a => a.Article.PublishedTime >= from && a.Article.PublishedTime <= to)
               .GroupBy(e=> e.EntityId.ToString());

            return result.ToDictionary(g => g.Key, g => g.Select(m => m.Article).ToCoreModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve mentioning articles for entities {entityIds}", entityIds);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public IEnumerable<Entity> SearchEntitiesAsync(string query)
    {
        using var trace = _activitySource.StartActivity("SearchEntities", ActivityKind.Internal);
        trace?.SetTag("query", query);

        try
        {
            var results = _dbContext.Entities
                .Where(e => EF.Functions.ILike(e.Name, $"%{query}%"));
            return results.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search entities with query '{query}'", query);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public (IEnumerable<(Entity Entity, int MentionCount)> Items, int TotalCount) GetEntitiesWithMentionCountAsync(
        DateOnly from, DateOnly to, int page, int pageSize, string? search = null)
    {
        using var trace = _activitySource.StartActivity("GetEntitiesWithMentionCount", ActivityKind.Internal);
        trace?.SetTag("from", from.ToString());
        trace?.SetTag("to", to.ToString());
        trace?.SetTag("page", page);
        trace?.SetTag("pageSize", pageSize);
        trace?.SetTag("search", search);

        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        try
        {
            var baseQuery = _dbContext.Entities
                .Where(e => e.Mentions.Any(m =>
                    m.Article.PublishedTime >= fromUtc &&
                    m.Article.PublishedTime <= toUtc));

            if (!string.IsNullOrWhiteSpace(search))
                baseQuery = baseQuery.Where(e => EF.Functions.ILike(e.Name, $"%{search}%"));

            var totalCount = baseQuery.Count();

            var items = baseQuery
                .Select(e => new
                {
                    Entity = e,
                    MentionCount = e.Mentions.Count(m =>
                        m.Article.PublishedTime >= fromUtc &&
                        m.Article.PublishedTime <= toUtc)
                })
                .OrderByDescending(x => x.MentionCount)
                .ThenBy(x => x.Entity.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToList();

            trace?.SetTag("totalCount", totalCount);
            return (items.Select(x => (x.Entity.ToCoreModel(), x.MentionCount)), totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve entities with mention counts");
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<IEnumerable<Entity>> GetAllEntitiesAsync(DateOnly startDate, DateOnly endDate)
    {
        using var trace = _activitySource.StartActivity("GetAllEntities", ActivityKind.Internal);
        trace?.SetTag("startDate", startDate.ToString());
        trace?.SetTag("endDate", endDate.ToString());

        try
        {
            var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

            var entities = await _dbContext.Entities
                .Where(e => e.Mentions.Any(m =>
                    m.Article.PublishedTime >= from &&
                    m.Article.PublishedTime <= to))
                .ToListAsync();

            trace?.SetTag("resultCount", entities.Count);
            return entities.ToCoreModel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all entities mentioned in articles between {startDate} and {endDate}", startDate, endDate);
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
