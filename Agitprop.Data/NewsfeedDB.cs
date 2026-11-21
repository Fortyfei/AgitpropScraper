using System;
using System.Diagnostics;

using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agitprop.Data;

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
    activity?.SetTag("article.source", article.SourceSite.ToString());

    try
    {
        _logger.LogDebug(
            "Creating mentions for article {Url} from {Source} at {PublishDate}",
            TelemetryUrl.RedactQueryAndFragment(url), article.SourceSite, article.PublishDate);

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 1️⃣ Article lekérdezése vagy létrehozása
            var articleToUse = await _db.Articles.FirstOrDefaultAsync(a => a.Url == url);
            var articleCreated = articleToUse is null;
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
                _logger.LogDebug("Created new article {ArticleId} for URL {Url}",
                    articleToUse.Id, TelemetryUrl.RedactQueryAndFragment(url));
            }
            else
            {
                _logger.LogDebug("Using existing article {ArticleId} for URL {Url}",
                    articleToUse.Id, TelemetryUrl.RedactQueryAndFragment(url));
            }

            activity?.SetTag("article.id", articleToUse.Id);

            // 2️⃣ Entitások batch lekérése
            var entityNames = entities.All.Select(e => e.Name).Distinct().ToList();
            var existingEntities = await _db.Entities
                .Where(e => entityNames.Contains(e.Name))
                .ToDictionaryAsync(e => e.Name);

            var mentionsToAdd = new List<PostgresMention>();
            int addedEntities = 0;
            int skippedMentions = 0;

            // 3️⃣ Már meglévő mentions betöltése a cikkhez
            var existingMentions = await _db.Mentions
                .Where(m => m.ArticleId == articleToUse.Id)
                .Select(m => m.EntityId)
                .ToHashSetAsync();

            foreach (var entity in entities.All.Distinct())
            {
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
                    _logger.LogDebug("Added new entity {EntityName} with Id {EntityId}", entity, dbEntity.Id);
                }

                // Duplikált mention ellenőrzése
                if (existingMentions.Contains(dbEntity.Id) || mentionsToAdd.Any(m => m.EntityId == dbEntity.Id))
                {
                    skippedMentions++;
                    _logger.LogDebug("Skipping mention for entity {EntityId} in article {ArticleId}", dbEntity.Id, articleToUse.Id);
                }
                else
                {
                    mentionsToAdd.Add(new PostgresMention
                    {
                        ArticleId = articleToUse.Id,
                        EntityId = dbEntity.Id
                    });
                    _logger.LogDebug("Prepared mention for entity {EntityName} in article {ArticleUrl}",
                        entity, TelemetryUrl.RedactQueryAndFragment(url));
                }
            }

            // Loop items are summarized on the enclosing activity: one span per entity made traces
            // grow with the entity vocabulary.
            activity?.SetTag("entities.scanned", entityNames.Count);
            activity?.SetTag("entities.added", addedEntities);
            activity?.SetTag("mentions.added", mentionsToAdd.Count);
            activity?.SetTag("mentions.skipped", skippedMentions);

            // 4️⃣ Mentions mentése
            if (mentionsToAdd.Count > 0)
                _db.Mentions.AddRange(mentionsToAdd);

            int changes;
            try
            {
                changes = await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsArticleUrlUniqueViolation(ex))
            {
                _db.ChangeTracker.Clear();
                _logger.LogDebug(
                    "Article already persisted by another worker; skipping duplicate URL {Url}",
                    TelemetryUrl.RedactQueryAndFragment(url));
                activity?.SetTag("article.duplicate", true);
                activity?.SetTag("outcome", "skipped");
                activity?.SetStatus(ActivityStatusCode.Ok, "Duplicate article skipped");
                NewsfeedDbMetrics.RecordArticlePersisted("duplicate");
                return 0;
            }

            _logger.LogDebug(
                "Finished creating mentions. Total entities added: {EntitiesAdded}, DB changes: {DbChanges}",
                addedEntities, changes);

            activity?.SetTag("db.changes", changes);
            activity?.SetTag("outcome", "success");
            activity?.SetStatus(ActivityStatusCode.Ok, "Mentions created");

            NewsfeedDbMetrics.RecordArticlePersisted(articleCreated ? "created" : "existing");
            NewsfeedDbMetrics.RecordEntitiesCreated(addedEntities);
            NewsfeedDbMetrics.RecordMentionsPersisted(mentionsToAdd.Count);

            return changes;
        });
    }
    catch (Exception ex)
    {
        // One event for the failure replaces the per-entity events; the type is bounded.
        activity?.AddEvent(new ActivityEvent(
            "create_mentions.failure",
            tags: new ActivityTagsCollection { { "exception.type", ex.GetType().Name } }));
        activity?.SetTag("outcome", "failure");
        activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
        NewsfeedDbMetrics.RecordDatabaseFailure("create_mentions");
        throw;
    }
}

internal static bool IsArticleUrlUniqueViolation(Exception exception)
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        if (current is PostgresException
            {
                SqlState: "23505",
                ConstraintName: "IX_articles_Url"
            })
        {
            return true;
        }
    }

    return false;
}

public async Task<bool> IsUrlAlreadyExists(string url)
    {
        using var activity = _activitySource.StartActivity("IsUrlAlreadyExists");

        try
        {
            bool exists = await _db.Articles.AnyAsync(a => a.Url == url);
            _logger.LogDebug("Checked existence of article {Url}: {Exists}",
                TelemetryUrl.RedactQueryAndFragment(url), exists);
            activity?.SetTag("article.exists", exists);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return exists;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            NewsfeedDbMetrics.RecordDatabaseFailure("is_url_already_exists");
            throw;
        }
    }
}
