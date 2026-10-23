# Agitprop.ServiceDefaults

## Purpose

A shared class library that provides **common service configuration** for the
consumer, RSS reader, Web API, and Web Client application processes. Each calls
`builder.AddServiceDefaults()` at startup. This is a thin configuration layer.

## Responsibilities

- **OpenTelemetry**: configures structured log export, HTTP/AspNetCore,
  process/runtime, MassTransit, and Entity Framework Core instrumentation.
  Application-owned ActivitySources and Meters are registered centrally so
  traces and metrics work consistently in every service using these defaults.
- **OTLP export**: enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured;
  Aspire supplies that endpoint for AppHost-managed services.
- **Health checks**: registers built-in health checks for all services.
- **HTTP client resilience**: configures the standard retry, timeout, and
  circuit-breaker pipeline for clients created by `IHttpClientFactory`.

## Retry boundaries

HTTP client defaults are not the only retry behavior in the application.
Keep these scopes distinct when changing retry behavior:

| Scope | Owner | Default behavior |
|-------|-------|------------------|
| `IHttpClientFactory` clients | `AddServiceDefaults()` | 3 retries; 90-second total request timeout |
| Direct page loading | `HttpStaticPageLoader` | `Retry:PageLoader` (3); retries exceptions and non-success responses |
| Browser loading | Puppeteer page loaders | 3 retries for browser download and navigation; separately configurable per loader |
| URL-existence checks, entity analysis and persistence | `NewsfeedSink` | `Retry:NewsfeedSink` (3) for each operation |
| Message consumption | `NewsfeedJobConsumerDefinition` | 3 retries for classified transient transport and PostgreSQL failures, at 500 ms, 2 seconds, and 5 seconds |
| Manual error recovery | CLI `retry` command | requeues messages; not an automatic retry policy |

The direct `RespectfulPageRequester` constructs its own `HttpClient` and does
not receive the `IHttpClientFactory` resilience handler. Factory-created
clients, such as the Web Client API client and proxy-provider clients, do.

The local Polly policies share their retry count and delay implementation via
`Agitprop.Infrastructure.RetryPolicyFactory`; their scope, triggers, and
configuration keys remain owned by the calling component.

The consumer classifies PostgreSQL connection failures (SQLSTATE class `08`)
and selected transient states (`40001`, `40P01`, `53300`, `55P03`, and `57P01`
through `57P03`) as retryable. Unique-constraint violations and other
permanent/unknown failures are not retried.

## Key File

- `Agitprop.ServiceDefaults/Extensions.cs` — the `AddServiceDefaults` extension
  method for `IHostApplicationBuilder`.

## Usage

```csharp
builder.AddServiceDefaults();
```

Called by: `Agitprop.Scraper.Consumer`, `Agitprop.Scraper.RssFeedReader`,
`Agitprop.Web.Api`, and `Agitprop.Web.Client`.

The scraping pipeline emits bounded metrics for consumed/completed/failed
jobs, scrape duration and outcomes, processed pages, RSS items/jobs, feed and
publish failures, and API cache hits/misses. URLs are not metric dimensions.
Application-owned URL fields in logs and spans omit query strings and
fragments. Automatic HttpClient instrumentation retains its own URL
attributes. EF Core spans do not include SQL text or query parameter values.
