using Microsoft.EntityFrameworkCore;

using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;
using Agitprop.Data.Models;

namespace Agitprop.Data;

public class ArticleFeedRepository : IArticleFeedRepository
{
    private readonly AppDbContext _dbContext;

    public ArticleFeedRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ArticleFeedPage GetArticleFeed(
        DateOnly startDate, DateOnly endDate, string? entityId, string? domain, string? search, int page, int pageSize)
    {
        var query = BuildQuery(startDate, endDate, entityId, domain);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(article => article.Title.ToLower().Contains(term));
        }

        var ordered = query
            .OrderByDescending(article => article.PublishedTime)
            .ThenBy(article => article.Id);

        List<Guid> pageIds;
        int total;
        if (string.IsNullOrWhiteSpace(domain))
        {
            total = ordered.Count();
            pageIds = ordered.Skip(page * pageSize).Take(pageSize).Select(article => article.Id).ToList();
        }
        else
        {
            // The SQL filter only narrows candidates; the exact publisher match happens here so paging stays correct.
            var matches = ordered.Select(article => new { article.Id, article.Url }).ToList()
                .Where(article => MatchesDomain(article.Url, domain))
                .Select(article => article.Id)
                .ToList();
            total = matches.Count;
            pageIds = matches.Skip(page * pageSize).Take(pageSize).ToList();
        }

        var articles = _dbContext.Articles
            .AsNoTracking()
            .Where(article => pageIds.Contains(article.Id))
            .Select(article => new
            {
                article.Id,
                article.Title,
                article.Url,
                article.PublishedTime,
                Entities = article.Mentions
                    .Select(mention => new { mention.Entity.Id, mention.Entity.Name, mention.Entity.Type })
                    .ToList()
            })
            .ToList();

        var items = pageIds
            .Select(id => articles.First(article => article.Id == id))
            .Select(article => new ArticleFeedItem(
                article.Id.ToString(),
                article.Title,
                article.Url,
                EntityRepository.NormalizeDomain(article.Url),
                article.PublishedTime,
                article.Entities
                    .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entity => new FeedEntity(entity.Id.ToString(), entity.Name, entity.Type))
                    .ToList()))
            .ToList();

        return new ArticleFeedPage(items, total);
    }

    public IReadOnlyList<DailyArticleCount> GetDailyArticleCounts(
        DateOnly startDate, DateOnly endDate, string? entityId, string? domain)
    {
        var rows = BuildQuery(startDate, endDate, entityId, domain)
            .Select(article => new { article.Url, article.PublishedTime })
            .ToList()
            .Where(article => string.IsNullOrWhiteSpace(domain) || MatchesDomain(article.Url, domain))
            .GroupBy(article => DateOnly.FromDateTime(article.PublishedTime))
            .ToDictionary(group => group.Key, group => group.Count());

        var days = new List<DailyArticleCount>();
        for (var day = startDate; day <= endDate; day = day.AddDays(1))
            days.Add(new DailyArticleCount(day, rows.GetValueOrDefault(day)));

        return days;
    }

    private IQueryable<PostgresArticle> BuildQuery(DateOnly startDate, DateOnly endDate, string? entityId, string? domain)
    {
        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        var query = _dbContext.Articles
            .AsNoTracking()
            .Where(article => article.PublishedTime >= from && article.PublishedTime <= to);

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var id = Guid.Parse(entityId);
            query = query.Where(article => article.Mentions.Any(mention => mention.EntityId == id));
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var needle = domain.Trim().ToLower();
            query = query.Where(article => article.Url.ToLower().Contains(needle));
        }

        return query;
    }

    private static bool MatchesDomain(string url, string domain) =>
        string.Equals(EntityRepository.NormalizeDomain(url), domain.Trim(), StringComparison.OrdinalIgnoreCase);
}
