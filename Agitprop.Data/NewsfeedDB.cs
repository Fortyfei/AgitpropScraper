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
                // A retried attempt must not inherit the entities added by the failed one.
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync();

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

                // 2️⃣ Felületi formák feloldása kanonikus entitásokra
                var resolutions = await ResolveAsync(entities);

                // 3️⃣ Hiányzó entitások létrehozása (versenybiztos INSERT ... ON CONFLICT)
                var createdEntities = await CreateMissingEntitiesAsync(resolutions);

                // 4️⃣ Már meglévő mentions betöltése a cikkhez
                var existingMentions = await _db.Mentions
                    .Where(m => m.ArticleId == articleToUse.Id)
                    .Select(m => m.EntityId)
                    .ToHashSetAsync();

                var mentionsToAdd = new List<PostgresMention>();
                int skippedMentions = 0;
                int rejectedEntities = 0;

                foreach (var resolution in resolutions)
                {
                    if (resolution.Kind == EntityResolutionKind.Rejected)
                    {
                        rejectedEntities++;
                        continue;
                    }

                    var entityId = resolution.Kind == EntityResolutionKind.Existing
                        ? resolution.EntityId!.Value
                        : createdEntities[new EntityKey(resolution.NormalizedSurface, resolution.Type)];

                    // Az existingMentions halmaz a most tervezett mentionöket is tartalmazza,
                    // így két felületi forma ugyanarra az entitásra egy mentiont ad.
                    if (!existingMentions.Add(entityId))
                    {
                        skippedMentions++;
                        _logger.LogDebug("Skipping mention for entity {EntityId} in article {ArticleId}",
                            entityId, articleToUse.Id);
                        continue;
                    }

                    mentionsToAdd.Add(new PostgresMention
                    {
                        ArticleId = articleToUse.Id,
                        EntityId = entityId,
                        SurfaceForm = resolution.SurfaceForm,
                        MatchKind = resolution.MatchKind
                    });
                    _logger.LogDebug("Prepared mention for entity {EntityName} in article {ArticleUrl}",
                        resolution.CanonicalName, TelemetryUrl.RedactQueryAndFragment(url));
                }

                var aliasMatches = resolutions.Count(r =>
                    r.Kind == EntityResolutionKind.Existing && r.MatchKind == EntityMatchKinds.Alias);
                var typeConflicts = resolutions.Count(r => r.TypeConflict);
                var ambiguousAliases = resolutions.Count(r => r.AliasAmbiguous);

                // Loop items are summarized on the enclosing activity: one span per entity made traces
                // grow with the entity vocabulary.
                activity?.SetTag("entities.scanned", resolutions.Count);
                activity?.SetTag("entities.created", createdEntities.Count);
                activity?.SetTag("entities.alias_matched", aliasMatches);
                activity?.SetTag("entities.type_conflict", typeConflicts);
                activity?.SetTag("entities.alias_ambiguous", ambiguousAliases);
                activity?.SetTag("entities.rejected", rejectedEntities);
                activity?.SetTag("mentions.added", mentionsToAdd.Count);
                activity?.SetTag("mentions.skipped", skippedMentions);

                // 5️⃣ Mentions mentése
                if (mentionsToAdd.Count > 0)
                {
                    _db.Mentions.AddRange(mentionsToAdd);
                }

                int changes;
                try
                {
                    changes = await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateException ex) when (IsArticleUrlUniqueViolation(ex))
                {
                    await RollBackAsync(transaction);
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
                catch (DbUpdateException ex) when (IsMentionDuplicateViolation(ex))
                {
                    await RollBackAsync(transaction);
                    _db.ChangeTracker.Clear();
                    _logger.LogDebug(
                        "Mentions already persisted by another worker; skipping duplicate URL {Url}",
                        TelemetryUrl.RedactQueryAndFragment(url));
                    activity?.SetTag("article.duplicate", true);
                    activity?.SetTag("outcome", "skipped");
                    activity?.SetStatus(ActivityStatusCode.Ok, "Duplicate mentions skipped");
                    NewsfeedDbMetrics.RecordArticlePersisted("duplicate");
                    return 0;
                }

                _logger.LogDebug(
                    "Finished creating mentions. Total entities added: {EntitiesAdded}, DB changes: {DbChanges}",
                    createdEntities.Count, changes);

                activity?.SetTag("db.changes", changes);
                activity?.SetTag("outcome", "success");
                activity?.SetStatus(ActivityStatusCode.Ok, "Mentions created");

                NewsfeedDbMetrics.RecordArticlePersisted(articleCreated ? "created" : "existing");
                NewsfeedDbMetrics.RecordEntitiesCreated(createdEntities.Count);
                NewsfeedDbMetrics.RecordMentionsPersisted(mentionsToAdd.Count);
                NewsfeedDbMetrics.RecordEntityResolutions("alias", aliasMatches);
                NewsfeedDbMetrics.RecordEntityResolutions("type_conflict", typeConflicts);
                NewsfeedDbMetrics.RecordEntityResolutions("ambiguous_alias", ambiguousAliases);
                NewsfeedDbMetrics.RecordEntityResolutions("rejected", rejectedEntities);

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
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<List<EntityResolution>> ResolveAsync(NamedEntityCollection entities)
    {
        var normalizedNames = entities.All
            .Select(entity => EntityNormalizer.Normalize(entity.Name))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var index = new EntityResolutionIndex();
        if (normalizedNames.Count > 0)
        {
            var existingEntities = await _db.Entities
                .Where(entity => normalizedNames.Contains(entity.NormalizedName))
                .AsNoTracking()
                .ToListAsync();
            foreach (var entity in existingEntities)
            {
                index.AddEntity(entity.Id, entity.Name, entity.NormalizedName, entity.Type);
            }

            var aliases = await _db.EntityAliases
                .Where(alias => normalizedNames.Contains(alias.NormalizedAlias))
                .Include(alias => alias.Entity)
                .AsNoTracking()
                .ToListAsync();
            foreach (var alias in aliases)
            {
                index.AddAlias(
                    alias.EntityId, alias.Entity.Name, alias.Entity.Type,
                    alias.NormalizedAlias, alias.Type, alias.Priority);
            }
        }

        // Két felületi forma, amelyik ugyanarra a normalizált alakra és típusra oldódik, egy mention.
        return entities.All
            .GroupBy(entity => (
                Name: EntityNormalizer.Normalize(entity.Name),
                Type: (entity.Type ?? string.Empty).Trim().ToUpperInvariant()))
            .Select(group => EntityResolver.Resolve(group.First(), index))
            .ToList();
    }

    private async Task<Dictionary<EntityKey, Guid>> CreateMissingEntitiesAsync(
        IReadOnlyList<EntityResolution> resolutions)
    {
        var created = new Dictionary<EntityKey, Guid>();
        var pending = new List<(EntityKey Key, EntityResolution Resolution)>();
        var seen = new HashSet<EntityKey>();

        foreach (var resolution in resolutions
            .Where(r => r.Kind == EntityResolutionKind.Create)
            .OrderBy(r => r.NormalizedSurface, StringComparer.Ordinal)
            .ThenBy(r => r.Type, StringComparer.Ordinal))
        {
            var key = new EntityKey(resolution.NormalizedSurface, resolution.Type);
            if (seen.Add(key))
            {
                pending.Add((key, resolution));
            }
        }

        if (pending.Count == 0)
        {
            return created;
        }

        foreach (var (key, resolution) in pending)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO entities ("Id", "Name", "NormalizedName", "Type", "Source")
                VALUES ({Guid.NewGuid()}, {resolution.CanonicalName}, {key.NormalizedName}, {key.Type}, {EntitySources.Observed})
                ON CONFLICT ("NormalizedName", "Type") DO NOTHING
                """);
        }

        // One round trip for the ids the inserts produced, whichever worker won the race.
        var surfaces = pending.Select(item => item.Key.NormalizedName).Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.Entities
            .Where(entity => surfaces.Contains(entity.NormalizedName))
            .Select(entity => new { entity.Id, entity.NormalizedName, entity.Type })
            .ToListAsync();
        var entityIds = rows.ToDictionary(
            row => new EntityKey(row.NormalizedName, row.Type),
            row => row.Id);

        foreach (var (key, resolution) in pending)
        {
            if (!entityIds.TryGetValue(key, out var entityId))
            {
                throw new InvalidOperationException(
                    $"Entity '{key.NormalizedName}' ({key.Type}) disappeared during creation.");
            }

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO entity_aliases ("Id", "EntityId", "Alias", "NormalizedAlias", "Type", "Priority")
                VALUES ({Guid.NewGuid()}, {entityId}, {resolution.CanonicalName}, {key.NormalizedName}, {key.Type}, 0)
                ON CONFLICT ("NormalizedAlias", "Type", "EntityId") DO NOTHING
                """);

            created[key] = entityId;
        }

        return created;
    }

    private static async Task RollBackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (InvalidOperationException)
        {
            // The provider already aborted the transaction after the constraint violation.
        }
    }

    internal static bool IsArticleUrlUniqueViolation(Exception exception) =>
        IsUniqueViolation(exception, "IX_articles_Url");

    internal static bool IsMentionDuplicateViolation(Exception exception) =>
        IsUniqueViolation(exception, "PK_mentions");

    private static bool IsUniqueViolation(Exception exception, string constraintName)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "23505" } postgres &&
                string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal))
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
