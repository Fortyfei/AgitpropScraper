# Agitprop.ServiceDefaults

## Purpose

A shared class library that provides **common service configuration** for all
ASP.NET Core and Aspire projects in the solution. All services call
`builder.AddServiceDefaults()` at startup. No other content — this is a thin
configuration layer.

## Responsibilities

- **OpenTelemetry**: configures structured log export, HTTP/AspNetCore,
  process/runtime, MassTransit, and Entity Framework Core instrumentation.
  Application-owned ActivitySources and Meters are registered centrally so
  traces and metrics work consistently in every service using these defaults.
- **OTLP export**: enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured;
  Aspire supplies that endpoint for AppHost-managed services.
- **Health checks**: registers built-in health checks for all services.
- **Resilience**: configures retry policies, circuit breakers, and HTTP
  client resilience via Polly extensions.

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
