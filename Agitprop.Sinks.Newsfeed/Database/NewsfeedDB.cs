using System;
using System.Diagnostics;

using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed.Database.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Agitprop.Sinks.Newsfeed.Database;

public class NewsfeedDB(AppDbContext db, ILogger<NewsfeedDB> logger) : INewsfeedDB
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<NewsfeedDB> _logger = logger;
    private static readonly ActivitySource _activitySource = new("Agitprop.NewsfeedDB");

public async Task<int> CreateMentionsAsync(
    string url,
    ContentParserResult article,
    NamedEntityCollection entities)
{
    using var activity = _activitySource.StartActivity("CreateMentionsAsync");
    activity?.SetBaggage("article.url", url);
    activity?.SetBaggage("article.source", article.SourceSite.ToString());
    activity?.SetBaggage("article.publishDate", article.PublishDate.ToString("o"));

    try
    {
        _logger.LogInformation(
            "Creating mentions for article {@Url} from {@Source} at {@PublishDate}",
            url, article.SourceSite, article.PublishDate);

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 1️⃣ Article lekérdezése vagy létrehozása
            using var articleActivity = _activitySource.StartActivity("ArticleLookup");
            articleActivity?.SetTag("article.url", url);

            var articleToUse = await _db.Articles.FirstOrDefaultAsync(a => a.Url == url);
            if (articleToUse == null)
            {
                articleToUse = new PostgresArticle
                {
                    Id = Guid.NewGuid(),
                    Title = article.Title,
                    Url = url,
                    PublishedTime = DateTime.SpecifyKind(article.PublishDate, DateTimeKind.Utc)
                };
                _db.Articles.Add(articleToUse);
                _logger.LogInformation("Created new article {ArticleId} for URL {Url}", articleToUse.Id, url);
            }
            else
            {
                _logger.LogDebug("Using existing article {ArticleId} for URL {Url}", articleToUse.Id, url);
            }

            articleActivity?.SetTag("article.id", articleToUse.Id);
            articleActivity?.SetStatus(ActivityStatusCode.Ok);

            // 2️⃣ Entitások batch lekérése
            var entityNames = entities.All.Select(e => e.Name).Distinct().ToList();
            var existingEntities = await _db.Entities
                .Where(e => entityNames.Contains(e.Name))
                .ToDictionaryAsync(e => e.Name);

            var mentionsToAdd = new List<PostgresMention>();
            int addedEntities = 0;

            // 3️⃣ Már meglévő mentions betöltése a cikkhez
            var existingMentions = await _db.Mentions
                .Where(m => m.ArticleId == articleToUse.Id)
                .Select(m => m.EntityId)
                .ToHashSetAsync();

            foreach (var entity in entities.All.Distinct())
            {
                using var entityActivity = _activitySource.StartActivity("ProcessEntity");
                entityActivity?.SetTag("entity.name", entity);

                // Entity létrehozása, ha nem létezik
                if (!existingEntities.TryGetValue(entity.Name, out var dbEntity))
                {
                    dbEntity = new PostgresEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = entity.Name,
                        Type = entity.Type
                    };
                    _db.Entities.Add(dbEntity);
                    existingEntities[entity.Name] = dbEntity;
                    addedEntities++;
                    _logger.LogDebug("Added new entity {@EntityName} with Id {@EntityId}", entity, dbEntity.Id);
                }

                entityActivity?.SetTag("entity.id", dbEntity.Id);

                // Duplikált mention ellenőrzése
                if (existingMentions.Contains(dbEntity.Id) || mentionsToAdd.Any(m => m.EntityId == dbEntity.Id))
                {
                    _logger.LogDebug("Skipping mention for entity {EntityId} in article {ArticleId}", dbEntity.Id, articleToUse.Id);
                    entityActivity?.SetTag("mention.skipped", true);
                }
                else
                {
                    mentionsToAdd.Add(new PostgresMention
                    {
                        ArticleId = articleToUse.Id,
                        EntityId = dbEntity.Id
                    });
                    entityActivity?.SetTag("mention.prepared", true);
                    _logger.LogDebug("Prepared mention for entity {@EntityName} in article {@ArticleUrl}", entity, url);
                }

                entityActivity?.SetStatus(ActivityStatusCode.Ok);
            }

            // 4️⃣ Mentions mentése
            if (mentionsToAdd.Count > 0)
                _db.Mentions.AddRange(mentionsToAdd);

            using var saveActivity = _activitySource.StartActivity("SaveMentions");
            saveActivity?.SetTag("mentions.to_add", mentionsToAdd.Count);
            saveActivity?.SetTag("entities.added", addedEntities);

            int changes = await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Finished creating mentions. Total entities added: {EntitiesAdded}, DB changes: {DbChanges}",
                addedEntities, changes);

            saveActivity?.SetTag("db.changes", changes);
            saveActivity?.SetStatus(ActivityStatusCode.Ok);

            activity?.SetStatus(ActivityStatusCode.Ok, "Mentions created");

            return changes;
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "Error creating mentions for article {@Url} from {@Source} at {@PublishDate}",
            url, article.SourceSite, article.PublishDate);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}

    public async Task<bool> IsUrlAlreadyExists(string url)
    {
        using var activity = _activitySource.StartActivity("IsUrlAlreadyExists");
        activity?.SetTag("article.url", url);

        try
        {
            bool exists = await _db.Articles.AnyAsync(a => a.Url == url);
            _logger.LogDebug("Checked existence of article {@Url}: {Exists}", url, exists);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if article exists: {@Url}", url);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
