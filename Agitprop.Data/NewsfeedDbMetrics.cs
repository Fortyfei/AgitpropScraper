using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agitprop.Data;

/// <summary>
/// Bounded business counters for the write path. Dimensions are limited to the outcome of the
/// operation so the series cannot grow with article or entity volume.
/// </summary>
public static class NewsfeedDbMetrics
{
    public const string MeterName = "Agitprop.NewsfeedDB";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ArticlesPersisted = Meter.CreateCounter<long>(
        "newsfeed.articles.persisted",
        description: "Articles seen by the write path, grouped by whether they were created, already existed or were skipped as a duplicate");

    private static readonly Counter<long> EntitiesCreated = Meter.CreateCounter<long>(
        "newsfeed.entities.created",
        description: "Entities created by the write path");

    private static readonly Counter<long> MentionsPersisted = Meter.CreateCounter<long>(
        "newsfeed.mentions.persisted",
        description: "Mentions persisted by the write path");

    private static readonly Counter<long> DatabaseFailures = Meter.CreateCounter<long>(
        "newsfeed.db.failures",
        description: "Database operations that failed, grouped by operation");

    private static readonly Counter<long> EntityResolutions = Meter.CreateCounter<long>(
        "newsfeed.entities.resolved",
        description: "Entity surface forms resolved by the write path, grouped by outcome (alias, type_conflict, ambiguous_alias, rejected)");

    public static void RecordArticlePersisted(string outcome) =>
        ArticlesPersisted.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordEntitiesCreated(int count)
    {
        if (count > 0)
        {
            EntitiesCreated.Add(count);
        }
    }

    public static void RecordMentionsPersisted(int count)
    {
        if (count > 0)
        {
            MentionsPersisted.Add(count);
        }
    }

    public static void RecordDatabaseFailure(string operation) =>
        DatabaseFailures.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordEntityResolutions(string outcome, int count)
    {
        if (count > 0)
        {
            EntityResolutions.Add(count, new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
