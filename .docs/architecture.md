# AgitpropScraper — Architecture

## 1. Overview

AgitpropScraper is a **modular monolith** orchestrated by **Aspire 13.4.2**. The diagram below shows the
runtime topology (container registry, services, and infrastructure). Every service
exposes OpenTelemetry OTLP (traces + metrics) and uses the shared
`Agitprop.ServiceDefaults` package for health checks and resilience.

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
        RABBIT[RabbitMQ: mgmt 15672 / AMQP 5672 external]
        PG[PostgreSQL: data volume, pgAdmin 5050, persistent lifetime]
    end

    %% Services
    subgraph "Services"
        NH[Newsfeed NLP Service: "uvicorn app:app on PORT 8111"]
        CON[Consumer: waits for newsfeedDb, messaging, nlpService]
        RSS[RSS Feed Reader: waits for messaging, Consumer]
        WEB_API[Web API: waits for newsfeedDb, messaging]
        WEB_CL[Web Client: waits for backend, external HTTP]
    end

    %% Connections
    REG -->|push| REG1
    REG1 -->|pull| RABBIT
    REG1 -->|pull| PG
    REG1 -->|pull| NH
    REG1 -->|pull| CON
    REG1 -->|pull| RSS
    REG1 -->|pull| WEB_API
    REG1 -->|pull| WEB_CL

    RABBIT -->|publish/subscribe| CON
    RABBIT -->|publish/subscribe| RSS
    RABBIT -->|publish/subscribe| WEB_API

    PG -->|EF Core| NH
    PG -->|EF Core| CON
    PG -->|EF Core| WEB_API

    NH -.->|OTLP gRPC| DIAG
    CON -.->|OTLP gRPC| DIAG
    RSS -.->|OTLP gRPC| DIAG
    WEB_API -.->|OTLP gRPC| DIAG
    WEB_CL -.->|HTTP| DIAG

    style NH fill:#e3f2fd,stroke:#1976d2,stroke-width:2px
    style CON fill:#e8f5e9,stroke:#388e3c,stroke-width:2px
    style RSS fill:#e8f5e9,stroke:#388e3c,stroke-width:2px
    style WEB_API fill:#fff3e0,stroke:#f57c00,stroke-width:2px
    style WEB_CL fill:#fff3e0,stroke:#f57c00,stroke-width:2px
    style PG fill:#f3e5f5,stroke:#8e24aa,stroke-width:2px
    style RABBIT fill:#fce4ec,stroke:#c2185b,stroke-width:2px
    style REG fill:#e0e0e0,stroke:#424242,stroke-width:1px
```

</details>

### Component Table

| # | Project | Kind | Waits for |
|---|---------|------|-----------|
| 1 | **Agitprop.AppHost** | Orchestrator (AppHost.cs) | — |
| 2 | **Agitprop.AppHost.App** | Full app (AppHost.cs) | postgres, newsfeedDb |
| 3 | **Agitprop.AppHost.Worker** | Worker variant | nlpService |
| 4 | **Agitprop.CORE** | Class library (interfaces/models) | — |
| 5 | **Agitprop.Infrastructure** | Class library (spiders, loaders, proxy pool) | — |
| 6 | **Agitprop.Infrastructure.Puppeteer** | Class library (Puppeteer loaders) | — |
| 7 | **Agitprop.Scraper.Consumer** | ASP.NET Core hosted service | newsfeedDb, messaging, nlpService; references all |
| 8 | **Agitprop.Scraper.RssFeedReader** | ASP.NET Core hosted service | messaging, Consumer |
| 9 | **Agitprop.Scraper.NLPService** | Python FastAPI (uvicorn) | — |
| 10 | **Agitprop.Web.Api** | ASP.NET Core Web API | newsfeedDb, messaging |
| 11 | **Agitprop.Web.Client** | Blazor WebAssembly | backend (Web.Api), external HTTP |
| 12 | **Agitprop.CLI** | System.CommandLine console | — |
| 13 | **Agitprop.ServiceDefaults** | Shared library (OTel, health checks, resilience) | — |
| 14 | **Agitprop.Sinks.Newsfeed** | EF Core + parsers | — |
| 15 | **Agitprop.Sinks.Newsfeed_Test** | xUnit test project | — |

## 2. Runtime Topology Walkthrough

The request flow follows these steps (see pipelines.md for sequence diagrams):

1. **RSS Feed Reader** (`Agitprop.Scraper.RssFeedReader`) pulls RSS feeds on a timer and publishes
   `ScrapingJobDescription` messages to RabbitMQ (`IPublishEndpoint.PublishBatch`).

2. **Consumer** (`Agitprop.Scraper.Consumer`) consumes jobs from the RabbitMQ queue. For each job
   it builds a `ScrapingJob` (via `ScrapingJobFactory`) and calls `ISpider.CrawlAsync()`.

3. **Spider** (`Agitprop.Infrastructure.Spider`) crawls the URL using `IPageTransport.LoadAsync()`.
   - If `PageCategory == TargetPage`, it runs content parsers and calls `ISink.EmitAsync()`.
   - Otherwise it runs link parsers + pagination to discover new URLs.

4. **Sink** (`Agitprop.Sinks.Newsfeed.NewsfeedSink`) receives the parsed `ContentParserResult`s
   and persists them to PostgreSQL via Entity Framework Core (`AppDbContext`).

5. **NLP Service** (`Agitprop.Scraper.NLPService`) is called (via `NamedEntityRecognizer`) to
   extract entities from the article text. Results are stored back to the same PostgreSQL
   database.

6. **Web API** (`Agitprop.Web.Api`) serves entity browse and analytics endpoints (cached, 15‑min
   cache for browse, 1‑hour for entity details). Blazor client consumes these endpoints.

7. **CLI** (`Agitprop.CLI`) provides manual operations: `scrape-article`, `scrape-archive`,
   `retry`.

## 3. Dependency Injection Summary

Key registered services (via `ConfigureInfrastructureWithoutBrowser` / `ConfigureInfrastructureWithBrowser`):

| Interface | Implementation | When registered |
|-----------|---------------|-----------------|
| `ISpider` | `Spider` | Always |
| `ICookieStorage` | `CookieStorage` | Always |
| `IStaticPageLoader` | `HttpStaticPageLoader` | Always |
| `IProxyProvider` | `ProxyScrapeProxyProvider` / `RedScrapeProxyProvider` | When `useProxies=true` |
| `IProxyPool` | `ProxyPoolService` | When `useProxies=true` |
| `RotatingHttpClientPool` | `RotatingHttpClientPool` | When `useProxies=true` |
| `IPageRequester` | `RotatingProxyPageRequester` / `RespectfulPageRequester` | When `useProxies=true` / default |
| `IBrowserPageLoader` | `PuppeteerPageLoader` / `PuppeteerPageLoaderWithProxies` | When browser mode + proxies |
| `IPageTransport` | Factory combining `IStaticPageLoader` + `IBrowserPageLoader` + `ILogger` | Always |
| `ISink` | `NewsfeedSink` | Always |
| MassTransit endpoints | `NewsfeedJobConsumer` + saga repo (in-memory) | Always |