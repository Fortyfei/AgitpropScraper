# Agitprop.ServiceDefaults

## Purpose

A shared class library that provides **common service configuration** for all
ASP.NET Core and Aspire projects in the solution. All services call
`builder.AddServiceDefaults()` at startup. No other content — this is a thin
configuration layer.

## Responsibilities

- **OpenTelemetry**: configures tracing, metrics, and OTLP export for every
  service (source/meter names like `"Agitprop.Spider"`, `"Agitprop.NewsfeedJobConsumer"`,
  `"Agitprop.RssFeedReader"`, `"Agitprop.Web.Api.Controllers.*"`).
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
`Agitprop.Web.Api`, `Agitprop.Web.Client`, and `Agitprop.AppHost.App/Worker`.
