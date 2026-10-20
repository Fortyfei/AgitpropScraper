# Agitprop.Web.Api

## Purpose

A **read-only ASP.NET Core 10 Web API** that serves scraped article and
entity analytics data to the Blazor client and external consumers.

## Key Features

- Swagger/OpenAPI documentation on `/swagger`.
- In-memory caching (`IMemoryCache`) with configurable durations.
- OpenTelemetry tracing and service discovery through shared service defaults.
- CORS allows all origins (internal network only).

## Controllers

### EntityController

- Route: `api/[controller]` (resolves to `api/entities`)
- ActivitySource: `"Agitprop.Web.Api.Controllers.EntitiesController"`
- Cache durations:
  - 15 min for browse / domain-stats
  - 1 hour for entity detail

| Endpoint | Method | Query Params | Returns |
|----------|--------|--------------|---------|
| `GetEntities` | GET | from, to, page, pageSize, search | `EntityBrowseResponse` |
| `GetEntityDomainStats` | GET `{id}/domain-stats` | from, to | `EntityDomainStatsResponse` |
| `GetEntityById` | GET `{id}` | — | `EntityResponse` |
| `GetArticles` | GET `{id}/articles` | — | `EntityArticlesResponse` |
| `GetCoMentions` | GET `{id}/co-mentions` | from, to, limit | `CoMentionsResponse` |

### AnalyticsController

- Route: `api/[controller]` (resolves to `api/analytics`)
- ActivitySource: `"Agitprop.Web.Api.Controllers.ActivitiesController"` (note:
  legacy name in source)
- Cache duration: 15 min

| Endpoint | Method | Query Params | Returns |
|----------|--------|--------------|---------|
| `GetTopMentionedEntities` | GET `topmentions` | from, to | `TopMentionedEntitiesResponse` |
| `GetSourceEntityCoverage` | GET `source-entities` | from, to, sourceLimit, entityLimit | `SourceEntityCoverageResponse` |

### SourcesController

- Route: `api/sources`
- Cache duration: 15 min

| Endpoint | Method | Query Params | Returns |
|----------|--------|--------------|---------|
| `GetSources` | GET | from, to | `SourcesResponse` |

### ArticlesController

- Route: `api/articles`
- Cache duration: 5 min (feed is not cached)
- Date range is limited to 366 days; `entityId` must be a GUID when supplied.

| Endpoint | Method | Query Params | Returns |
|----------|--------|--------------|---------|
| `GetFeed` | GET `feed` | from, to, entityId, domain, search, page, pageSize (max 100) | `ArticleFeedResponse` (newest first) |
| `GetDailyCounts` | GET `daily` | from, to, entityId, domain | `DailyCountsResponse` (zero-filled days, `isSpike` flag) |
| `GetFirstPublishers` | GET `first-publishers` | from, to, entityId | `FirstPublishersResponse` (publishers by earliest stored article) |

A day is a spike when it has at least 3 articles and sits more than two
standard deviations above the window average; windows under 5 days are never
flagged (`SpikeDetector`). Publisher order reflects `PublishedTime` only, since
ingestion time is not stored.

## DI Registration (Program.cs)

1. `AddServiceDefaults()` — shared OTel, health, service discovery, and HTTP resilience config.
2. `AddOpenApi()` + `AddSwaggerGen()` — OpenAPI generation.
3. `AddNewsfeedRepositories()` — registers `IEntityRepository`,
   `ITrendingRepository`, `INewsfeedDB`.
4. `AddControllers()` — MVC controllers.
5. `AddMemoryCache()` — in-memory response cache.
6. `AddCors()` — allows any origin/method/header.
7. Applies EF migrations at startup.

The API reads PostgreSQL only; RabbitMQ is not required for API startup.

## DTOs (`DTOs/` folder)

- `EntityBrowseResponse`, `EntityBrowseItem`
- `EntityDomainStatsResponse`, `DomainStatDto`
- `EntityResponse`
- `EntityArticlesResponse`, `ArticleDto`
- `TopMentionedEntitiesResponse`, `TopMentionedEntity`
- `CoMentionsResponse`, `CoMentionItem`
- `SourceEntityCoverageResponse`, `SourceCoverageItem`, `SourceEntityCoverageItem`
- `SourcesResponse`, `SourceStatDto`

## Startup Validation

Requires `ConnectionStrings:newsfeed` (PostgreSQL).

## Project Path

`Agitprop.Web.Api/Controllers/EntitiesController.cs`
`Agitprop.Web.Api/Controllers/AnalyticsController.cs`
