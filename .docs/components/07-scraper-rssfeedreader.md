# Agitprop.Scraper.RssFeedReader

## Purpose

An **ASP.NET Core hosted service** that periodically pulls RSS feeds from
configured URLs and publishes `NewsfeedJobDescrpition` messages to
**RabbitMQ** for downstream processing by the Consumer.

The job contract is defined in the shared Core project, so the RSS reader does
not depend on the Newsfeed sink project.

## Responsibilities

1. On a configurable interval (default `IntervalMinutes = 60`), fetches
   each configured RSS feed using `System.ServiceModel.Syndication`.
2. Parses feed items into `ScrapingJobDescription` objects with URL and
   `PageContentType = Article`.
3. Publishes the batch of jobs to RabbitMQ via
   `IPublishEndpoint.PublishBatch(jobs)`.

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Host setup: `AddServiceDefaults()`, `AddHostedService<RssFeedReader>()`, MassTransit, OpenTelemetry |
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
- Activity kinds: `Producer` for publish, `Consumer` for feed processing
- Feed spans include a bounded item count rather than a trace event per item;
  individual jobs are traced through message publishing and consumption.

## Startup

Started by `Agitprop.AppHost` as project `rss-feed-reader`. It waits for
RabbitMQ and for the consumer to start.

Requires `ConnectionStrings:messaging` (RabbitMQ AMQP).
