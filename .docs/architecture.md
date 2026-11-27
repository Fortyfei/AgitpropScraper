# AgitpropScraper — Architecture

## 1. Overview

AgitpropScraper is a distributed .NET application orchestrated by **Aspire 13.5.4**.
Its service processes share class libraries for scraping, persistence, and NLP.
AppHost-managed services use `Agitprop.ServiceDefaults` for health checks,
instrumentation, and HTTP client resilience; the standalone CLI writes structured
console logs and does not export OTLP telemetry.

<details>
<summary>Click to expand runtime topology diagram</summary>

```mermaid
flowchart TB
    subgraph "Container Registry"
        REG[ghcr.io/fortyfei/agitprop]
    end

    subgraph "Aspire Dashboard"
        DIAG[http://localhost:18888]
    end

    %% Infrastructure
    subgraph "Infra"
        REG1[ghcr registry auth]
        PG[PostgreSQL 17 + PGMQ extension: data volume, pgAdmin 5050]
    end

    %% Services
    subgraph "Services"
        CON[Consumer: in-process Hungarian NER, waits for newsfeedDb]
        RSS[RSS Feed Reader: waits for newsfeedDb, Consumer]
        WEB[Web app: Blazor Server, owns repositories and migrations, external HTTP]
    end

    %% Connections
    REG -->|push| REG1
    REG1 -->|pull| PG
    REG1 -->|pull| CON
    REG1 -->|pull| RSS
    REG1 -->|pull| WEB

    PG -->|PGMQ queue + EF Core| CON
    PG -->|PGMQ queue| RSS
    PG -->|EF Core| WEB

    CON -.->|OTLP gRPC| DIAG
    RSS -.->|OTLP gRPC| DIAG
    WEB -.->|OTLP gRPC| DIAG

    style CON fill:#e8f5e9,stroke:#388e3c,stroke-width:2px
    style RSS fill:#e8f5e9,stroke:#388e3c,stroke-width:2px
    style WEB fill:#fff3e0,stroke:#f57c00,stroke-width:2px
    style PG fill:#f3e5f5,stroke:#8e24aa,stroke-width:2px
    style REG fill:#e0e0e0,stroke:#424242,stroke-width:1px
```

</details>

### Component Table

| # | Project | Kind | Waits for |
|---|---------|------|-----------|
| 1 | **Agitprop.AppHost** | Orchestrator (AppHost.cs) | — |
| 2 | **Agitprop.Testing.AppHost** | Integration-test AppHost | — |
| 3 | **Agitprop.Core** | Class library (interfaces, models, message contract) | — |
| 4 | **Agitprop.Infrastructure** | Class library (spider, HTTP loaders, proxy pool) | — |
| 5 | **Agitprop.Infrastructure.Puppeteer** | Class library (browser loaders) | — |
| 6 | **Agitprop.Scraper.Consumer** | Worker with in-process Hungarian NER | newsfeedDb |
| 7 | **Agitprop.Scraper.RssFeedReader** | RSS-to-pgmq worker | newsfeedDb, consumer |
| 8 | **Agitprop.Scraper.NLPService** | .NET class library (ONNX Runtime, huBERT NER) | — |
| 9 | **Agitprop.Web** | Blazor Web App (Interactive Server); owns the analytics queries, repositories, and migrations | newsfeedDb |
| 10 | **Agitprop.CLI** | System.CommandLine operator tool | — |
| 11 | **Agitprop.ServiceDefaults** | Shared library (OTel, health checks, HTTP resilience) | — |
| 12 | **Agitprop.Sinks.Newsfeed** | `NewsfeedSink` + NLP wiring (uses Data) | — |
| 12a | **Agitprop.Sources** | Per-site `INewsfeedSource`s, parsers, `NewsfeedPipeline` | — |
| 12b | **Agitprop.Data** | EF Core context, migrations, repositories | — |
| 12c | **Agitprop.Messaging** | Npgsql-backed PGMQ client | PostgreSQL |
| 13 | **Agitprop.Sinks.Newsfeed_Test** | Parser fixture tests | — |
| 14 | **Agitprop.UnitTests** | Unit tests | — |
| 15 | **Agitprop.IntegrationTests** | Analytics/PostgreSQL integration tests | Testing.AppHost |

## 2. Runtime Topology Walkthrough

The request flow follows these steps (see pipelines.md for sequence diagrams):

1. **RSS Feed Reader** (`Agitprop.Scraper.RssFeedReader`) pulls RSS feeds on a timer and writes
   `NewsfeedJobDescrpition` messages to the `newsfeed_job` PGMQ queue in PostgreSQL.

2. **Consumer** (`Agitprop.Scraper.Consumer`) claims jobs using PGMQ's visibility timeout. For each job
   it builds a `ScrapingJob` (via `ScrapingJobFactory`) and calls `ISpider.CrawlAsync()`.

3. **Spider** (`Agitprop.Infrastructure.Spider`) crawls the URL using `IPageTransport.LoadAsync()`.
   - If `PageCategory == TargetPage`, it runs content parsers and calls `ISink.EmitAsync()`.
   - Otherwise it runs link parsers + pagination to discover new URLs.

4. **Sink** (`Agitprop.Sinks.Newsfeed.NewsfeedSink`) receives the parsed `ContentParserResult`s
   and persists them to PostgreSQL via Entity Framework Core (`AppDbContext`).

5. The sink calls the in-process `NamedEntityRecognizer` in
   `Agitprop.Scraper.NLPService` to extract Hungarian entities from article text.
   There is no NLP HTTP service. The model is downloaded and checksum-verified
   on first use, or loaded from a pre-provisioned directory. The write path then
   resolves every surface form through `EntityResolver` against the
   `entities`/`entity_aliases` tables, so inflected forms share one canonical
   entity while same-spelling homographs with different types stay separate.
   Results are stored in PostgreSQL.

6. **Web app** (`Agitprop.Web`) is a Blazor Server host that answers the dashboard
   queries straight from the repositories through `AnalyticsService` (cached, 15‑min for
   list queries, 1‑hour for entity details) and applies EF Core migrations on startup.

7. **CLI** (`Agitprop.CLI`) provides manual operations: `scrape-article`, `scrape-archive`,
   `retry`.

## 3. Dependency Injection Summary

Key registered services (via `ConfigureInfrastructureWithoutBrowser` / `ConfigureInfrastructureWithBrowser`):

| Interface | Implementation | When registered |
|-----------|---------------|-----------------|
| `ISpider` | `Spider` | Always |
| `ICookiesStorage` | `CookieStorage` | Always |
| `IStaticPageLoader` | `HttpStaticPageLoader` | Always |
| `IProxyProvider` | `ProxyScrapeProxyProvider` / `RedScrapeProxyProvider` | When `useProxies=true` |
| `IProxyPool` | `ProxyPool` | When `useProxies=true` |
| `RotatingHttpClientPool` | `RotatingHttpClientPool` | When `useProxies=true` |
| `IPageRequester` | `RotatingProxyPageRequester` / `RespectfulPageRequester` | When `useProxies=true` / default |
| `IBrowserPageLoader` | `PuppeteerPageLoader` / `PuppeteerPageLoaderWithProxies` | When browser mode + proxies |
| `IPageTransport` | Factory combining `IStaticPageLoader` + `IBrowserPageLoader` + `ILogger` | Always |
| `ISink` | `NewsfeedSink` | Always |
| `IPgmqClient` | `PgmqClient` over Npgsql | Consumer, RSS reader, CLI |