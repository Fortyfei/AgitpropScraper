# AgitpropScraper — Pipelines

This document contains **Mermaid sequence diagrams** for every major pipeline in
the system, plus a step-by-step runtime walkthrough.

---

## 1. RSS Feed → Queue Pipeline

```mermaid
sequenceDiagram
    participant RSS as RssFeedReader (IHostedService)
    participant DB as PostgreSQL + PGMQ
    participant CON as Consumer (NewsfeedJobConsumer)

    loop On interval (default 60 min)
        RSS->>RSS: Fetch RSS feeds (System.ServiceModel.Syndication)
        RSS->>RSS: Parse feeds → List<NewsfeedJobDescrpition>
        RSS->>DB: SendBatchAsync(newsfeed_job, jobs)
        DB-->>CON: read() claims visible jobs
    end
```

## 2. Job Consumption → Spider Crawl Pipeline

```mermaid
sequenceDiagram
    participant MQ as PostgreSQL + PGMQ
    participant CON as NewsfeedJobConsumer
    participant FACT as ScrapingJobFactory
    participant SPIDER as Spider (ISpider)
    participant TRANSPORT as PageTransport (IPageTransport)
    participant BROWSER as Puppeteer (optional)
    participant HTTP as HttpStaticPageLoader

    MQ->>CON: read(newsfeed_job, vt) claims job
    CON->>FACT: GetArticleScrapingJob(source, url)
    FACT-->>CON: ScrapingJob (ContentParsers, LinkParsers, Paginator)
    CON->>SPIDER: CrawlAsync(job, sink, ct)
    SPIDER->>TRANSPORT: LoadAsync(url, PageLoadOptions)
    alt RequiresJavaScript=true
        TRANSPORT->>BROWSER: LoadAsync(url, pageActions, headless)
        BROWSER-->>TRANSPORT: string content
    else Direct HTTP
        TRANSPORT->>HTTP: Load(url)
        HTTP-->>TRANSPORT: string content
    end
    TRANSPORT-->>SPIDER: PageLoadResult(content, url, strategy, duration)
```

## 3. Target Page Parsing → Sink Pipeline

```mermaid
sequenceDiagram
    participant SPIDER as Spider (ISpider)
    participant PARSE as BaseArticleContentParser
    participant SINK as NewsfeedSink (ISink)
    participant NLP as NamedEntityRecognizer
    participant DB as PostgreSQL (AppDbContext)

    SPIDER->>SPIDER: ProcessTargetPage(job, html, sink)
    loop For each IContentParser
        SPIDER->>PARSE: ParseContentAsync(html)
        PARSE-->>SPIDER: ContentParserResult(text, title, date)
        SPIDER->>SINK: EmitAsync(url, results, ct)
        SINK->>NLP: AnalyzeSingleAsync(article text)
        NLP-->>SINK: NamedEntityCollection (PER/LOC/ORG/MISC)
        SINK->>DB: CreateMentionsAsync(url, result, entities)
        DB-->>SINK: rows inserted
    end
    SINK-->>SPIDER: Emit complete
```

## 4. Link Discovery & Pagination Pipeline

```mermaid
sequenceDiagram
    participant SPIDER as Spider (ISpider)
    participant LINK as ILinkParser
    participant PAGINATOR as IPaginator
    participant CON as NewsfeedJobConsumer
    participant MQ as PostgreSQL + PGMQ

    SPIDER->>SPIDER: ProcessPage(job, html)
    loop For each ILinkParser
        SPIDER->>LINK: GetLinksAsync(baseUrl, doc)
        LINK-->>SPIDER: List<ScrapingJobDescription>
    end
    SPIDER->>PAGINATOR: GetNextPageAsync(currentUrl, doc)
    PAGINATOR-->>SPIDER: next ScrapingJobDescription
    SPIDER-->>CON: return discovered jobs
    CON->>MQ: SendBatchAsync(newsfeed_job, new jobs)
```

## 5. In-Process NLP Pipeline

```mermaid
sequenceDiagram
    participant SINK as NewsfeedSink
    participant NER as INamedEntityRecognizer
    participant ONNX as ONNX Runtime / Hungarian huBERT
    participant DB as PostgreSQL

    SINK->>NER: AnalyzeSingleAsync(article text)
    NER->>ONNX: tokenize and infer (overlapping windows)
    ONNX-->>NER: BIO label logits
    NER-->>SINK: NamedEntityCollection (surface forms)
    SINK->>DB: Persist entities and mentions
```

## 6. Web API → Blazor Client Pipeline

```mermaid
sequenceDiagram
    participant CLIENT as Blazor Web App
    participant API as Web Api (Controllers)
    participant REPO as EntityRepository (IEntityRepository)
    participant TREND as TrendingRepository (ITrendingRepository)
    participant CACHE as IMemoryCache
    participant DB as PostgreSQL

    CLIENT->>API: GET api/entities?from=&to=&page=&pageSize=
    API->>CACHE: TryGetValue(cacheKey)
    alt Cache hit
        CACHE-->>API: cached response
    else Cache miss
        API->>REPO: GetEntitiesWithMentionCountAsync(from,to,page,pageSize,search)
        REPO->>DB: SQL query
        DB-->>REPO: entities + mention counts
        REPO-->>API: (items, totalCount)
        API->>CACHE: Set(cacheKey, response, 15min)
    end
    API-->>CLIENT: EntityBrowseResponse

    Note over CLIENT,API: Entity detail (1‑hour cache)
    CLIENT->>API: GET api/entities/{id}
    API->>CACHE: TryGetValue($"entity:{id}")
    alt Cache hit
        CACHE-->>API: cached response
    else Cache miss
        API->>REPO: GetEntityByIdAsync(id)
        REPO->>DB: SQL query
        DB-->>REPO: Entity
        API->>CACHE: Set(cacheKey, response, 1hour)
    end
    API-->>CLIENT: EntityResponse

    Note over CLIENT,API: Top mentions analytics
    CLIENT->>API: GET api/analytics/topmentions?from=&to=
    API->>TREND: GetTrendingEntitiesAsync(from,to).Take(8)
    TREND->>DB: SQL query
    DB-->>TREND: trending entities
    API->>REPO: GetMentioningArticlesAsync(entityIds, from,to)
    REPO->>DB: SQL query
    DB-->>REPO: mention counts
    API->>CACHE: Set(cacheKey, response, 15min)
    API-->>CLIENT: TopMentionedEntitiesResponse
```

## 7. CLI Pipeline (scrape-archive)

```mermaid
sequenceDiagram
    participant CLI as Agitprop.CLI (dotnet)
    participant ORCH as ScrapeCommandOrchestrator
    participant RESOLVE as ArchiveCommandInputResolver
    participant PUBLISHER as IArticleJobPublisher
    participant MQ as PostgreSQL + PGMQ

    CLI->>CLI: dotnet agitprop scrape-archive --date 2024-01-01
    CLI->>RESOLVE: Resolve(rawInput)
    RESOLVE-->>CLI: ArchiveCommandResolvedInput
    CLI->>ORCH: ScrapeArchiveAsync(resolved, ct)
    ORCH->>PUBLISHER: PublishAsync(articles, ct)
    PUBLISHER->>MQ: SendBatchAsync(newsfeed_job, jobs)
    MQ-->>PUBLISHER: message IDs
    PUBLISHER-->>ORCH: PublishExecutionResult
    ORCH-->>CLI: Console output (success/fail)
```

## 8. CLI Retry Pipeline

```mermaid
sequenceDiagram
    participant CLI as Agitprop.CLI (dotnet)
    participant ORCH as ScrapeCommandOrchestrator
    participant PUBLISHER as IArticleJobPublisher (PgmqArticleJobPublisher)
    participant DB as PostgreSQL + PGMQ

    CLI->>CLI: dotnet agitprop retry --connection <PostgreSQL-connection-string> --failed-queue newsfeed_job_error --target-queue newsfeed_job --max 10
    CLI->>ORCH: RetryFailedFeedsAsync(request, ct)
    ORCH->>PUBLISHER: RetryFailedFeedsAsync(request, ct)
    PUBLISHER->>DB: read(newsfeed_job_error)
    DB-->>PUBLISHER: failed messages
    PUBLISHER->>DB: send(target) + archive(source) in transaction
    PUBLISHER-->>ORCH: RetryFailedFeedsExecutionResult
    ORCH-->>CLI: Console summary (success/fail)
```

---

## Runtime Step-by-Step Walkthrough

### Step 1: Aspire Startup

```
aspire start --apphost Agitprop.AppHost/Agitprop.AppHost.csproj
```

1. **AppHost** creates the `DistributedApplication` builder.
2. Registers the container registry `ghcr.io/fortyfei/agitprop` (via `WithEnvironmentAwareImagePush`).
3. Creates the docker-compose environment `agitprop` with:
   - **Dashboard** on `:18888`
   - **PostgreSQL 17 + PGMQ** (`postgres`) — data volume, pgAdmin `:5050`, persistent lifetime, OTLP
   - **Database** `newsfeed` attached to PostgreSQL
   - **Consumer**, **RSS Feed Reader**, **Web API**, **Web Client** projects
4. Each service waits only for its declared dependencies (see component table in architecture.md).

### Step 2: Service Startup Order

1. **PostgreSQL** + **Database** start first.
2. **Consumer** waits for `newsfeedDb`, provisions or loads the Hungarian ONNX model, creates the PGMQ extension and queues, then starts its polling worker.
3. **RSS Feed Reader** waits for `newsfeedDb` and `Consumer`. Registers as `IHostedService`.
5. **Web API** waits for `newsfeedDb`. Applies migrations at startup when configured, then maps controllers.
6. **Web Client** waits for backend (Web API). The Blazor host supports Interactive Server and WebAssembly rendering.

### Step 3: Runtime Operation

1. **RSS Feed Reader** fetches feeds on interval → `IPgmqClient.SendBatchAsync`.
2. **Consumer** claims messages from `newsfeed_job` → `ScrapingJobFactory.GetArticleScrapingJob` → `ISpider.CrawlAsync`.
3. **Spider** uses `IPageTransport.LoadAsync` → `HttpStaticPageLoader` (or `PuppeteerPageLoader` if browser mode).
4. **BaseArticleContentParser** extracts date/title/lead/article via XPath fallback lists.
5. **NewsfeedSink** calls the in-process `NamedEntityRecognizer`, then persists results to PostgreSQL via `AppDbContext.CreateMentionsAsync`.
6. **NamedEntityRecognizer** runs the local ONNX NER model and returns a `NamedEntityCollection`.
7. **Web API** serves entity browse/analytics from PostgreSQL (cached 15 min / 1 hour).
8. **CLI** can re-queue failed messages from `newsfeed_job_error` via the `retry` command.