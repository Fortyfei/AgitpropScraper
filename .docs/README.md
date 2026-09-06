# AgitpropScraper — Developer Documentation

Internal developer documentation for the **AgitpropScraper** project: a distributed
news-article scraping and named-entity analytics platform built on .NET 10, Aspire 13,
MassTransit/RabbitMQ, PuppeteerSharp, PostgreSQL, and a Python NLP microservice
(spaCy).

## Table of Contents

| Document | Purpose |
|----------|---------|
| [requirements.md](requirements.md) | Functional and non-functional requirements. |
| [architecture.md](architecture.md) | System overview, component map, runtime topology. |
| [pipelines.md](pipelines.md) | End-to-end sequence diagrams for every pipeline. |
| [development.md](development.md) | Build, test, and local development setup. |
| [deployment.md](deployment.md) | Aspire orchestration, Docker, CI/CD, GHCR publishing. |
| [components/](components/) | One page per project in the solution. |

## Component Pages

- [AppHost](components/01-apphost.md) — full distributed app (AppHost + App + Worker).
- [Core](components/02-core.md) — shared models, enums, and interfaces.
- [Infrastructure](components/03-infrastructure.md) — spiders, HTTP loaders, proxy pool.
- [Infrastructure.Puppeteer](components/04-infrastructure-puppeteer.md) — PuppeteerSharp browser loaders.
- [Sinks.Newsfeed](components/05-sinks-newsfeed.md) — site-specific parsers, paginators, EF Core sink.
- [Scraper.Consumer](components/06-scraper-consumer.md) — MassTransit consumer that runs the spider.
- [Scraper.RssFeedReader](components/07-scraper-rssfeedreader.md) — RSS-to-queue producer.
- [Scraper.NLPService](components/08-scraper-nlpservice.md) — Python FastAPI / spaCy NER service.
- [Web.Api](components/09-web-api.md) — read-only analytics REST API.
- [Web.Client](components/10-web-client.md) — Blazor WebAssembly dashboard.
- [CLI](components/11-cli.md) — `dotnet` operator tool.
- [ServiceDefaults](components/12-service-defaults.md) — shared OpenTelemetry/health/resilience config.
- [Infrastructure.Postgres](components/13-infrastructure-postgres.md) — shared PostgreSQL data layer.

## Quick Start

```bash
# Start the entire distributed app (Aspire dashboard on http://localhost:18888)
dotnet run --project Agitprop.AppHost/Agitprop.AppHost.csproj
```

For full instructions see [development.md](development.md).
