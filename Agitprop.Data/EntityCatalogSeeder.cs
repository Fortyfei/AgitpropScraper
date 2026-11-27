using Agitprop.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Agitprop.Data;

/// <summary>
/// Makes the curated <see cref="EntityCatalog"/> the authority for the surface forms it lists.
/// Idempotent: it inserts missing catalog entities and aliases, promotes an observed entity that
/// already carries a catalog name to catalog source, and folds an observed entity whose normalized
/// name denotes exactly one catalog entity into it (re-pointing mentions first, with the
/// (ArticleId, EntityId) collisions dropped). A form shared by several catalog entities — a
/// homograph such as the river and party Tisza — is left alone. Safe to run from every service at
/// startup.
/// </summary>
public static class EntityCatalogSeeder
{
    public static async Task TrySeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await SeedAsync(db, logger, cancellationToken);
        }
        catch (Exception exception) when (FindPostgresException(exception) is { } postgres
            && postgres.SqlState is "42P01" or "42703")
        {
            // 42P01 = table missing, 42703 = column missing: the database is not migrated yet, and
            // the web app owns migrations. Startup must not fail on that ordering.
            logger.LogWarning(
                "Skipping entity catalog seeding because the schema is not migrated yet (SQLSTATE {SqlState})",
                FindPostgresException(exception)!.SqlState);
        }
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var catalog = EntityCatalog.All;
        var normalizedNames = catalog
            .SelectMany(entity => entity.Aliases
                .Select(alias => EntityNormalizer.Normalize(alias.Alias))
                .Append(EntityNormalizer.Normalize(entity.Name)))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var created = (Entities: 0, Aliases: 0);

        try
        {
            // The context is configured with a retrying execution strategy, which rejects
            // user-initiated transactions. Creating the transaction inside the strategy's
            // delegate keeps the whole seed one retriable unit.
            var strategy = db.Database.CreateExecutionStrategy();
            created = await strategy.ExecuteAsync(
                () => SeedInTransactionAsync(db, logger, catalog, normalizedNames, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Entity catalog already seeded by another instance");
            return;
        }

        if (created.Entities > 0 || created.Aliases > 0)
        {
            logger.LogInformation(
                "Entity catalog seeded: {EntitiesCreated} entities and {AliasesCreated} aliases created",
                created.Entities,
                created.Aliases);
        }
    }

    private static async Task<(int Entities, int Aliases)> SeedInTransactionAsync(
        AppDbContext db,
        ILogger logger,
        IReadOnlyList<CatalogEntity> catalog,
        HashSet<string> normalizedNames,
        CancellationToken cancellationToken)
    {
        // The entity rows carried over from a retried attempt are re-read by this method.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existing = await db.Entities
            .Where(entity => normalizedNames.Contains(entity.NormalizedName))
            .ToListAsync(cancellationToken);

        var catalogEntities = new Dictionary<(Guid Id, string Type), PostgresEntity>();
        var inserted = 0;

        foreach (var entry in catalog)
        {
            var normalizedName = EntityNormalizer.Normalize(entry.Name);
            var entity = existing.FirstOrDefault(candidate =>
                candidate.NormalizedName == normalizedName && candidate.Type == entry.Type);

            if (entity is null)
            {
                entity = new PostgresEntity
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    NormalizedName = normalizedName,
                    Type = entry.Type,
                    Source = EntitySources.Catalog
                };
                db.Entities.Add(entity);
                existing.Add(entity);
                inserted++;
            }
            else if (entity.Source != EntitySources.Catalog)
            {
                // The catalog owns the canonical spelling of its entities.
                entity.Source = EntitySources.Catalog;
                entity.Name = entry.Name;
            }

            catalogEntities[(entry.Id, entry.Type)] = entity;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Every normalized surface form a catalog entity answers to. A form listed by exactly one
        // catalog entity is the same subject; homographs such as the river and party Tisza map to
        // two entities and must stay separate.
        var catalogTargets = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var catalogById = new Dictionary<Guid, PostgresEntity>();
        foreach (var entry in catalog)
        {
            var target = catalogEntities[(entry.Id, entry.Type)];
            catalogById[target.Id] = target;

            foreach (var alias in entry.Aliases.Append(new CatalogAlias(entry.Name)))
            {
                var normalizedAlias = EntityNormalizer.Normalize(alias.Alias);
                if (normalizedAlias.Length == 0)
                {
                    continue;
                }

                if (!catalogTargets.TryGetValue(normalizedAlias, out var targets))
                {
                    targets = [];
                    catalogTargets[normalizedAlias] = targets;
                }

                targets.Add(target.Id);
            }
        }

        // Observed stand-ins for a catalog entity are folded into it, including ones whose NER type
        // drifted: the resolver would otherwise keep matching the exact (name, type) row first and
        // the subject would stay split.
        foreach (var shadow in existing.Where(entity => entity.Source != EntitySources.Catalog).ToList())
        {
            if (!catalogTargets.TryGetValue(shadow.NormalizedName, out var targets) || targets.Count != 1)
            {
                continue;
            }

            await FoldAsync(db, shadow, catalogById[targets.Single()], logger, cancellationToken);
            existing.Remove(shadow);
        }

        var desiredAliases = catalog
            .SelectMany(entry => entry.Aliases
                .Append(new CatalogAlias(entry.Name))
                .Select(alias => new
                {
                    Entity = catalogEntities[(entry.Id, entry.Type)],
                    Alias = alias.Alias,
                    NormalizedAlias = EntityNormalizer.Normalize(alias.Alias),
                    Type = entry.Type,
                    alias.Priority
                }))
            .Where(alias => alias.NormalizedAlias.Length > 0)
            .DistinctBy(alias => (alias.NormalizedAlias, alias.Type, alias.Entity.Id))
            .ToList();

        var desiredNames = desiredAliases.Select(alias => alias.NormalizedAlias).ToHashSet(StringComparer.Ordinal);
        var existingAliases = await db.EntityAliases
            .Where(alias => desiredNames.Contains(alias.NormalizedAlias))
            .ToListAsync(cancellationToken);

        var addedAliases = 0;
        foreach (var desired in desiredAliases)
        {
            var exists = existingAliases.Any(alias =>
                alias.NormalizedAlias == desired.NormalizedAlias &&
                alias.Type == desired.Type &&
                alias.EntityId == desired.Entity.Id);

            if (exists)
            {
                continue;
            }

            db.EntityAliases.Add(new PostgresEntityAlias
            {
                Id = Guid.NewGuid(),
                EntityId = desired.Entity.Id,
                Alias = desired.Alias,
                NormalizedAlias = desired.NormalizedAlias,
                Type = desired.Type,
                Priority = desired.Priority
            });
            addedAliases++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (inserted, addedAliases);
    }

    private static async Task FoldAsync(
        AppDbContext db,
        PostgresEntity shadow,
        PostgresEntity target,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Lock the shadow row first: the FK check of a concurrent mention insert takes FOR KEY SHARE
        // on it, which conflicts with FOR UPDATE. Without the lock a mention committed between the
        // re-point and the delete is lost, or the entity delete fails with a foreign key violation.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM entities WHERE \"Id\" = {shadow.Id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            db.Entry(shadow).State = EntityState.Detached;
            return;
        }

        // Re-point and delete in one statement so no mention can survive the fold.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH moved AS (
                DELETE FROM mentions WHERE "EntityId" = {shadow.Id}
                RETURNING "ArticleId", "SurfaceForm", "MatchKind"
            )
            INSERT INTO mentions ("ArticleId", "EntityId", "SurfaceForm", "MatchKind")
            SELECT "ArticleId", {target.Id}, "SurfaceForm", "MatchKind" FROM moved
            ON CONFLICT ("ArticleId", "EntityId") DO NOTHING
            """, cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM entity_aliases WHERE \"EntityId\" = {shadow.Id}", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM entities WHERE \"Id\" = {shadow.Id}", cancellationToken);
        db.Entry(shadow).State = EntityState.Detached;

        logger.LogInformation(
            "Folded observed entity {ShadowName} into catalog entity {TargetName}",
            shadow.Name,
            target.Name);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "23505" })
            {
                return true;
            }
        }

        return false;
    }
}
