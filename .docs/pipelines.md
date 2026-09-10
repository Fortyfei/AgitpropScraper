# AgitpropScraper — Pipelines

This document contains **Mermaid sequence diagrams** for every major pipeline in
the system, plus a step-by-step runtime walkthrough.

---

## 1. RSS Feed → Queue Pipeline

```mermaid
sequenceDiagram
    participant RSS as RssFeedReader (IHostedService)
    participant MQ as RabbitMQ (MessageBroker)
    participant CON as Consumer (NewsfeedJobConsumer)

    loop On interval (default 60 min)
        RSS->>RSS: Fetch RSS feeds (System.ServiceModel.Syndication)
        RSS->>RSS: Parse feeds → List<NewsfeedJobDescription>
        RSS->>MQ: IPublishEndpoint.PublishBatch(jobs)
        MQ-->>CON: Queue message delivered
    end
```

## 2. Job Consumption → Spider Crawl Pipeline

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant CON as NewsfeedJobConsumer
    participant FACT as ScrapingJobFactory
    participant SPIDER as Spider (ISpider)
    participant TRANSPORT as PageTransport (IPageTransport)
    participant BROWSER as Puppeteer (optional)
    participant HTTP as HttpStaticPageLoader

    MQ->>CON: Job message delivered
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
    participant NLP as NamedEntityRecognizer
    participant SINK as NewsfeedSink (ISink)
    participant DB as PostgreSQL (AppDbContext)

    SPIDER->>SPIDER: ProcessTargetPage(job, html, sink)
    loop For each IContentParser
        SPIDER->>PARSE: ParseContentAsync(html)
        PARSE-->>SPIDER: ContentParserResult(text, title, date)
        SPIDER->>NLP: AnalyzeSingleAsync(corpus)
        NLP-->>SPIDER: NamedEntityCollection (PER/LOC/ORG/MISC)
        SPIDER->>SINK: EmitAsync(url, results, ct)
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
    participant SINK as NewsfeedSink (ISink)
    participant MQ as RabbitMQ

    SPIDER->>SPIDER: ProcessPage(job, html)
    loop For each ILinkParser
        SPIDER->>LINK: GetLinksAsync(baseUrl, doc)
        LINK-->>SPIDER: List<ScrapingJobDescription>
    end
    SPIDER->>PAGINATOR: GetNextPageAsync(currentUrl, doc)
    PAGINATOR-->>SPIDER: next ScrapingJobDescription
    SPIDER->>SINK: CheckPageAlreadyVisited(url)
    SINK-->>SPIDER: bool exists
    alt !exists
        SPIDER->>MQ: PublishBatch(nextJobs)
    end
```

## 5. NLP Service Pipeline

```mermaid
sequenceDiagram
    participant CLIENT as NamedEntityRecognizer
    participant SVC as NLP Service (FastAPI)
    participant NLP as spaCy (hu_core_news_lg)

    CLIENT->>SVC: POST /analyzeSingle { text }
    SVC->>NLP: nlp(text)
    NLP-->>SVC: NamedEntityCollection
    SVC-->>CLIENT: List<NamedEntity>

    Note over CLIENT,SVC: Batch variant
    CLIENT->>SVC: POST /analyzeBatch { texts[] }
    SVC->>NLP: nlp.pipe(texts)
    NLP-->>SVC: NamedEntityCollection[]
    SVC-->>CLIENT: NamedEntityCollection[]

    Note over CLIENT,SVC: Health check
    CLIENT->>SVC: GET /health
    SVC-->>CLIENT: { status: "ok" }
```

## 6. Web API → Blazor Client Pipeline

```mermaid
sequenceDiagram
    participant CLIENT as Blazor WebAssembly
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
    participant MQ as RabbitMQ

    CLI->>CLI: dotnet agitprop scrape-archive --date 2024-01-01
    CLI->>RESOLVE: Resolve(rawInput)
    RESOLVE-->>CLI: ArchiveCommandResolvedInput
    CLI->>ORCH: ScrapeArchiveAsync(resolved, ct)
    ORCH->>PUBLISHER: PublishAsync(articles, ct)
    PUBLISHER->>MQ: PublishBatch(jobs)
    MQ-->>PUBLISHER: ack
    PUBLISHER-->>ORCH: PublishExecutionResult
    ORCH-->>CLI: Console output (success/fail)
```

## 8. CLI Retry Pipeline

```mermaid
sequenceDiagram
    participant CLI as Agitprop.CLI (dotnet)
    participant ORCH as ScrapeCommandOrchestrator
    participant PUBLISHER as IArticleJobPublisher (RabbitMqPublisher)
    participant MQ as RabbitMQ (Error Queue)
    participant TARGET as Target Queue

    CLI->>CLI: dotnet agitprop retry --failedQueue errQueue --targetQueue mainQueue --max 10
    CLI->>ORCH: RetryFailedFeedsAsync(request, ct)
    ORCH->>PUBLISHER: RetryFailedFeedsAsync(request, ct)
    PUBLISHER->>MQ: Read failed messages (maxN)
    MQ-->>PUBLISHER: messages
    PUBLISHER->>TARGET: Republish to target queue
    TARGET-->>MQ: ack
    PUBLISHER-->>ORCH: RetryFailedFeedsExecutionResult
    ORCH-->>CLI: Console summary (success/fail)
```

---

## Runtime Step-by-Step Walkthrough

### Step 1: Aspire Startup

```
dotnet run --project Agitprop.AppHost/Agitprop.AppHost.csproj
```

1. **AppHost** creates the `DistributedApplication` builder.
2. Registers the container registry `ghcr.io/fortyfei/agitprop` (via `WithEnvironmentAwareImagePush`).
3. Creates the docker-compose environment `agitprop` with:
   - **Dashboard** on `:18888`
   - **RabbitMQ** (`messaging`) — mgmt `:15672`, AMQP `:5672`, OTLP
   - **PostgreSQL** (`postgres`) — data volume, pgAdmin `:5050`, persistent lifetime, OTLP
   - **Database** `newsfeed` attached to PostgreSQL
   - **NLP service** (`nlpservice`) — `uvicorn app:app`, health check `:8111/health`
   - **Consumer**, **RSS Feed Reader**, **Web API**, **Web Client** projects
4. Each service waits for its dependencies (see component table in architecture.md).

### Step 2: Service Startup Order

1. **RabbitMQ** starts first (infrastructure).
2. **PostgreSQL** + **Database** start next.
3. **NLP Service** (`nlpservice`) starts and loads `hu_core_news_lg` model.
4. **Consumer** waits for `newsfeedDb`, `messaging`, `nlpService`. Applies EF migrations if Development or `ApplyMigrationsAtStartup=true`. Registers MassTransit endpoints.
5. **RSS Feed Reader** waits for `messaging`, `Consumer`. Registers as `IHostedService`.
6. **Web API** waits for `newsfeedDb`, `messaging`. Applies migrations. Maps controllers.
7. **Web Client** waits for backend (Web API) + external HTTP. Blazor InteractiveServer rendering.

### Step 3: Runtime Operation

1. **RSS Feed Reader** fetches feeds on interval → `IPublishEndpoint.PublishBatch`.
2. **Consumer** consumes from queue → `ScrapingJobFactory.GetArticleScrapingJob` → `ISpider.CrawlAsync`.
3. **Spider** uses `IPageTransport.LoadAsync` → `HttpStaticPageLoader` (or `PuppeteerPageLoader` if browser mode).
4. **BaseArticleContentParser** extracts date/title/lead/article via XPath fallback lists.
5. **NamedEntityRecognizer** POSTs to NLP service `/analyzeSingle` → returns `NamedEntityCollection`.
6. **NewsfeedSink** persists to PostgreSQL via `AppDbContext.CreateMentionsAsync`.
7. **Web API** serves entity browse/analytics from PostgreSQL (cached 15 min / 1 hour).
8. **CLI** can re-queue failed messages via `retry` command.