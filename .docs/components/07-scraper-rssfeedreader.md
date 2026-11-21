# Agitprop.Scraper.RssFeedReader

## Purpose

An **ASP.NET Core hosted service** that periodically pulls RSS feeds from
configured URLs and publishes `NewsfeedJobDescrpition` messages to the
**PGMQ queue in PostgreSQL** for downstream processing by the Consumer.

The job contract is defined in the shared Core project, so the RSS reader does
not depend on the Newsfeed sink project.

## Responsibilities

1. On a configurable interval (default `IntervalMinutes = 60`), fetches
   each configured RSS feed using `System.ServiceModel.Syndication`.
2. Parses feed items into `ScrapingJobDescription` objects with URL and
   `PageContentType = Article`.
3. Writes the batch of jobs to `newsfeed_job` via `IPgmqClient.SendBatchAsync`.

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: `AddServiceDefaults()`, `AddPgmqClient("newsfeed")`, `AddHostedService<RssFeedReader>()`, OpenTelemetry |
| `RssFeedReader.cs` | Implements `IHostedService` — `StartAsync`, `ExecuteAsync`, `StopAsync` |

## Configuration

In `appsettings.json`:

```json
{
  "Feeds": [
    "https://example.com/feed1.xml",
    "https://example.com/feed2.xml"
  ],
  "IntervalMinutes": 60
}
```

## Observability

- ActivitySource: `"Agitprop.RssFeedReader"`
- OpenTelemetry tracing + metrics
- One `rss.cycle` root span per cycle; the hosted service keeps no span open for
  its lifetime, so a cycle trace stays bounded to the feeds it read and the jobs
  it published.
- Activity kinds are `Internal`: a cycle is an application operation, not a queue
  consumer. The real `Producer` boundary is the publish span that `PgmqClient`
  creates for every message, so each job starts its own trace.
- Feed spans carry a bounded item count and `feed.host` rather than the feed URL,
  and produce no trace event per item. A cycle whose feeds partly fail reports the
  `partial_failure` outcome instead of `success`.
- `rss.cycle` uses explicit duration buckets and always survives root sampling.

## Startup

Started by `Agitprop.AppHost` as project `rss-feed-reader`. It waits for
PostgreSQL and for the consumer to start.

Requires `ConnectionStrings:newsfeed` (PostgreSQL with PGMQ enabled).
