# Agitprop.Web.Api

## Purpose

A **read-only ASP.NET Core 10 Web API** that serves scraped article and
entity analytics data to the Blazor client and external consumers.

## Key Features

- Swagger/OpenAPI documentation on `/swagger`.
- In-memory caching (`IMemoryCache`) with configurable durations.
- OpenTelemetry tracing (`ConfigureWebApiTracing`).
- Service discovery integration.
- CORS allows all origins (internal network only).

## Controllers

### EntitiesController

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

### AnalyticsController

- Route: `api/[controller]` (resolves to `api/analytics`)
- ActivitySource: `"Agitprop.Web.Api.Controllers.ActivitiesController"` (note:
  legacy name in source)
- Cache duration: 15 min

| Endpoint | Method | Query Params | Returns |
|----------|--------|--------------|---------|
| `GetTopMentionedEntities` | GET `topmentions` | from, to | `TopMentionedEntitiesResponse` |

## DI Registration (Program.cs)

1. `AddServiceDefaults()` — shared OTel / health / resilience config.
2. `ConfigureWebApiTracing()` — Web API specific tracing.
3. `AddServiceDiscovery()` — service discovery client.
4. `AddOpenApi()` + `AddSwaggerGen()` — OpenAPI generation.
5. `AddNewsfeedRepositories()` — registers `IEntityRepository`,
   `ITrendingRepository`, `INewsfeedDB`.
6. `AddControllers()` — MVC controllers.
7. `AddMemoryCache()` — in-memory response cache.
8. `AddCors()` — allows any origin/method/header.
9. Applies EF migrations at startup.

## DTOs (`DTOs/` folder)

- `EntityBrowseResponse`, `EntityBrowseItem`
- `EntityDomainStatsResponse`, `DomainStatDto`
- `EntityResponse`
- `EntityArticlesResponse`, `ArticleDto`
- `TopMentionedEntitiesResponse`, `TopMentionedEntity`

## Startup Validation

Requires `ConnectionStrings:newsfeed` (PostgreSQL).

## Project Path

`Agitprop.Web.Api/Controllers/EntitiesController.cs`
`Agitprop.Web.Api/Controllers/AnalyticsController.cs`
