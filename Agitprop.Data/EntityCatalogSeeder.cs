using Agitprop.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Agitprop.Data;

/// <summary>
/// Makes the curated <see cref="EntityCatalog"/> the authority for the surface forms it lists.
/// Idempotent and concurrency-safe: entities and aliases are written with
/// <c>INSERT ... ON CONFLICT DO NOTHING</c>, so two services seeding at the same time, or a row that
/// already exists under another id, can never abort the catalog. An observed entity whose normalized
/// name denotes exactly one catalog entity is folded into it — including when the NER type drifted —
/// while a form shared by several catalog entities (the river and party <c>Tisza</c>) is left alone.
/// Safe to run from every service at startup.
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

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var catalog = EntityCatalog.All;
        var normalizedNames = catalog
            .SelectMany(entity => entity.Aliases
                .Select(alias => EntityNormalizer.Normalize(alias.Alias))
                .Append(EntityNormalizer.Normalize(entity.Name)))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // The context is configured with a retrying execution strategy, which rejects user-initiated
        // transactions. Creating the transaction inside the strategy's delegate keeps the whole seed
        // one retriable unit.
        var strategy = db.Database.CreateExecutionStrategy();
        var created = await strategy.ExecuteAsync(
            () => SeedInTransactionAsync(db, logger, catalog, normalizedNames, cancellationToken));

        if (created.Entities > 0 || created.Aliases > 0)
        {
            logger.LogInformation(
                "Entity catalog seeded: {EntitiesAdded} entities and {AliasesAdded} aliases added",
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
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var entitiesBefore = await CountCatalogEntitiesAsync(db, cancellationToken);
        var aliasesBefore = await CountCatalogAliasesAsync(db, cancellationToken);

        foreach (var entry in catalog)
        {
            var normalizedName = EntityNormalizer.Normalize(entry.Name);

            // Raw insert instead of an EF batch: one conflicting row must not abort the catalog, and
            // a concurrent seeder must be harmless.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO entities ("Id", "Name", "NormalizedName", "Type", "Source")
                VALUES ({entry.Id}, {entry.Name}, {normalizedName}, {entry.Type}, {EntitySources.Catalog})
                ON CONFLICT ("NormalizedName", "Type") DO NOTHING
                """, cancellationToken);

            // A row that already carried this identity becomes the catalog entity, and the catalog
            // owns its canonical spelling.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE entities
                SET "Source" = {EntitySources.Catalog}, "Name" = {entry.Name}
                WHERE "NormalizedName" = {normalizedName} AND "Type" = {entry.Type}
                  AND "Source" <> {EntitySources.Catalog}
                """, cancellationToken);
        }

        var rows = await db.Entities
            .Where(entity => normalizedNames.Contains(entity.NormalizedName))
            .AsNoTracking()
            .Select(entity => new { entity.Id, entity.Name, entity.NormalizedName, entity.Type, entity.Source })
            .ToListAsync(cancellationToken);

        var catalogByKey = rows
            .Where(row => row.Source == EntitySources.Catalog)
            .ToDictionary(row => new EntityKey(row.NormalizedName, row.Type), row => row);

        // Every normalized surface form a catalog entity answers to. A form listed by exactly one
        // catalog entity is the same subject; homographs map to two entities and must stay separate.
        var catalogTargets = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var aliases = new List<(Guid EntityId, string Alias, string NormalizedAlias, string Type, int Priority)>();
        foreach (var entry in catalog)
        {
            var normalizedName = EntityNormalizer.Normalize(entry.Name);
            if (!catalogByKey.TryGetValue(new EntityKey(normalizedName, entry.Type), out var target))
            {
                throw new InvalidOperationException(
                    $"Catalog entity '{entry.Name}' ({entry.Type}) could not be persisted.");
            }

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
                aliases.Add((target.Id, alias.Alias, normalizedAlias, entry.Type, alias.Priority));
            }
        }

        // Observed stand-ins for a catalog entity are folded into it, including ones whose NER type
        // drifted: the resolver would otherwise keep matching the exact (name, type) row first and
        // the subject would stay split.
        foreach (var shadow in rows.Where(row => row.Source != EntitySources.Catalog))
        {
            if (!catalogTargets.TryGetValue(shadow.NormalizedName, out var targets) || targets.Count != 1)
            {
                continue;
            }

            var target = catalogByKey.Values.Single(row => row.Id == targets.Single());
            await FoldAsync(db, shadow.Id, shadow.Name, target.Id, target.Name, logger, cancellationToken);
        }

        foreach (var alias in aliases.DistinctBy(item => (item.NormalizedAlias, item.Type, item.EntityId)))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO entity_aliases ("Id", "EntityId", "Alias", "NormalizedAlias", "Type", "Priority")
                VALUES ({Guid.NewGuid()}, {alias.EntityId}, {alias.Alias}, {alias.NormalizedAlias}, {alias.Type}, {alias.Priority})
                ON CONFLICT ("NormalizedAlias", "Type", "EntityId") DO NOTHING
                """, cancellationToken);
        }

        var entitiesAfter = await CountCatalogEntitiesAsync(db, cancellationToken);
        var aliasesAfter = await CountCatalogAliasesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (entitiesAfter - entitiesBefore, aliasesAfter - aliasesBefore);
    }

    private static Task<int> CountCatalogEntitiesAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Entities.CountAsync(entity => entity.Source == EntitySources.Catalog, cancellationToken);

    private static Task<int> CountCatalogAliasesAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.EntityAliases.CountAsync(
            alias => db.Entities.Any(entity =>
                entity.Id == alias.EntityId && entity.Source == EntitySources.Catalog),
            cancellationToken);

    private static async Task FoldAsync(
        AppDbContext db,
        Guid shadowId,
        string shadowName,
        Guid targetId,
        string targetName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Lock the shadow row first: the FK check of a concurrent mention insert takes FOR KEY SHARE
        // on it, which conflicts with FOR UPDATE. Without the lock a mention committed between the
        // re-point and the delete is lost, or the entity delete fails with a foreign key violation.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM entities WHERE \"Id\" = {shadowId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return;
        }

        // Re-point and delete in one statement so no mention can survive the fold.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH moved AS (
                DELETE FROM mentions WHERE "EntityId" = {shadowId}
                RETURNING "ArticleId", "SurfaceForm", "MatchKind"
            )
            INSERT INTO mentions ("ArticleId", "EntityId", "SurfaceForm", "MatchKind")
            SELECT "ArticleId", {targetId}, "SurfaceForm", "MatchKind" FROM moved
            ON CONFLICT ("ArticleId", "EntityId") DO NOTHING
            """, cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM entity_aliases WHERE \"EntityId\" = {shadowId}", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM entities WHERE \"Id\" = {shadowId}", cancellationToken);

        logger.LogInformation(
            "Folded observed entity {ShadowName} into catalog entity {TargetName}",
            shadowName,
            targetName);
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
}
