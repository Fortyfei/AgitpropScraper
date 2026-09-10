# Agitprop.Scraper.RssFeedReader

## Purpose

An **ASP.NET Core hosted service** that periodically pulls RSS feeds from
configured URLs and publishes `ScrapingJobDescription` messages to
**RabbitMQ** for downstream processing by the Consumer.

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

## Startup

Started by `Agitprop.AppHost` as project `rssreader`:

```csharp
var rssReader = builder.AddProject<Projects.Agitprop_Scraper_RssFeedReader>("rssreader")
    .WithReference(messaging).WaitFor(messaging)
    .WaitFor(consumer);
```

Requires `ConnectionStrings:messaging` (RabbitMQ AMQP).
