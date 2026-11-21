# Agitprop.ServiceDefaults

## Purpose

A shared class library that provides **common service configuration** for the
consumer, RSS reader, and web application processes. Each calls
`builder.AddServiceDefaults()` at startup. This is a thin configuration layer.

## Responsibilities

- **OpenTelemetry**: configures structured log export, HTTP/AspNetCore,
  process/runtime, and Entity Framework Core instrumentation. Blazor's own
  `Route`/`Event` activities, the SignalR `ComponentHub` activities, and static-file
  requests are excluded so a user interaction stays a single trace instead of
  producing unrelated single-span traces.
  Application-owned ActivitySources and Meters are registered centrally so
  traces and metrics work consistently in every service using these defaults.

  Only one database layer is traced by default: EF Core spans name the
  application operation that issued the query, while Npgsql spans would duplicate
  each command. Enable the second layer with `Observability:EnableNpgsqlTracing`
  when the raw command is what you need to see.

  `BoundedTraceSampler` inherits a parent decision and otherwise samples new
  root spans by `Observability:TraceSampleRatio` (default `1.0`). Roots that
  anchor a bounded trace - an `rss.cycle` root and a `Server` root - are always
  kept. Error spans inside a kept trace are preserved; a root that fails after it
  was sampled out cannot be recovered, so this ratio is the volume knob.

  Span attribute and event limits default to 64 attributes, 1024 characters per
  value, and 16 events. Deployments can override the matching `OTEL_SPAN_*`
  environment variables.

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
| Message consumption | PGMQ worker | 3 retries for classified transient transport and PostgreSQL failures, delayed by 1, 2, and 5 seconds |
| Manual error recovery | CLI `retry` command | requeues messages; not an automatic retry policy |

The direct `RespectfulPageRequester` constructs its own `HttpClient` and does
not receive the `IHttpClientFactory` resilience handler. Factory-created
clients, such as the proxy-provider clients, do.

The local Polly policies share their retry count and delay implementation via
`Agitprop.Infrastructure.RetryPolicyFactory`; their scope, triggers, and
configuration keys remain owned by the calling component.

PGMQ delay values are whole seconds, so the former 500 ms first retry is rounded to 1 second.
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
and `Agitprop.Web`.

The scraping pipeline emits bounded metrics for consumed/completed/failed
jobs, scrape duration and outcomes, processed pages, RSS items/jobs, feed and
publish failures, queue depth and oldest-message age, retries and dead letters
by failure class, lease-renewal failures, in-flight messages, and dashboard
query cache hits/misses. URLs, entity names, message IDs, and free-text search
terms are not metric dimensions. Application-owned URL fields in logs and spans
omit query strings and fragments; spans use `server.address` instead of full
URLs. Duration histograms for job, page, RSS-cycle, request, and rate-limit wait
times use explicit 100 ms to 300 s buckets. Automatic HttpClient instrumentation
retains its own URL attributes. EF Core spans do not include SQL text or query
parameter values.
