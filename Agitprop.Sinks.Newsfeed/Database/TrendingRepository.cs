using System;
using System.Diagnostics;

using Agitprop.Core.Interfaces;
using Agitprop.Core.Models;

using Microsoft.Extensions.Logging;

namespace Agitprop.Sinks.Newsfeed.Database;

public class TrendingRepository : ITrendingRepository
{
    private readonly AppDbContext _dbContext;


    private readonly ILogger<TrendingRepository> _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.Repository.TrendingRepository");

    public TrendingRepository(AppDbContext dbContext, ILogger<TrendingRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public IEnumerable<Entity> GetTrendingEntitiesAsync(DateOnly startDate, DateOnly endDate, int topN)
    {
        using var trace = _activitySource.StartActivity("GetTrendingEntitiesAsync", ActivityKind.Internal);
        trace?.SetTag("startDate", startDate.ToString());
        trace?.SetTag("endDate", endDate.ToString());

        var from = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(endDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        var entities = _dbContext.Entities
            .Where(e => e.Mentions.Any(m =>
                m.Article.PublishedTime >= from &&
                m.Article.PublishedTime <= to))
                .OrderByDescending(e => e.Mentions.Count)
                .Take(topN);

        _logger.LogInformation("Found {Count} trending entities between {From} and {To}", entities.Count(), from, to);
        trace?.SetTag("foundEntities", entities.ToList());

        return entities.ToCoreModel();
    }
}
