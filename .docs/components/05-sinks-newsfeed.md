# Agitprop.Sinks.Newsfeed

## Purpose

The **sink layer** — the `NewsfeedSink` (persist + NLP enrichment) and its DI
wiring (`AddNewsfeedSink`).

> The source side and the database were split out:
> - **`Agitprop.Sources`** — one `INewsfeedSource` per site (`Sites/*Source.cs`),
>   `NewsfeedSourceCatalog`, `NewsfeedPipeline` (`INewsfeedPipeline`), and the
>   parsers/paginators below (`Scrapers/`). Register with `AddNewsfeedSources()`;
>   restrict sites via config `Newsfeed:Sources:Enabled` (array of ids).
>   Adding a site = adding one source class + listing it in `NewsfeedSources.BuiltIn()`.
> - **`Agitprop.Data`** — EF Core context, models, migrations, repositories
>   (`AddNewsfeedDB`, `AddNewsfeedRepositories`). Namespaces: `Agitprop.Data`,
>   `Agitprop.Data.Models`, `Agitprop.Data.Migrations`.
> - Contracts (`INewsfeedSource`, `INewsfeedPipeline`, `SourceId`) live in Core.
>
> The sections below describing parsers and the database now live in those projects.

## Contents

### Content Parsers (`Agitprop.Sources/Scrapers/ContentParsers/`)

All extend the abstract `BaseArticleContentParser : IContentParser`.

| Parser | Source Site |
|--------|-------------|
| `BaseArticleContentParser.cs` | Base class — XPath fallback extraction |
| `IndexArticleContentParser.cs` | Index |
| `HvgArticleContentParser.cs` | HVG |
| `HuszonnegyArticleContentParser.cs` | 24.hu |
| `AlfahirArticleContentParser.cs` | Alfahir |
| `MerceArticleContentParser.cs` | Mérce |
| `BlikkArticleContentParser.cs` | Blikk |
| `KontrollArticleContentParser.cs` | Kontroll (archive = single rolling sitemap, ~1 week) |
| `MandinerArticleContentParser.cs` | Mandiner |
| `MagyarNemzetArticleContentParser.cs` | Magyar Nemzet |
| `MetropolArticleContentParser.cs` | Metropol |
| `NegynegynegyArticleContentParser.cs` | 444.hu |
| `TelexArticleContentParser.cs` | Telex |
| `RtlArticleContentParser.cs` | RTL |
| `RipostArticleContentParser.cs` | Ripost |
| `OrigoArticleContentParser.cs` | Origó |

#### BaseArticleContentParser

- Abstract properties: `DateXPaths`, `TitleXPaths`, `LeadXPaths`,
  `ArticleXPaths` (lists of XPath strings for fallback extraction).
- `SourceSite` (abstract `SourceId`).
- Falls back to common published-time metadata and JSON-LD `datePublished`,
  `og:title`, article-body containers, and JSON-LD `articleBody` when a site's
  primary selectors do not match.
- The Index parser excludes scripts/styles, the Google preference banner,
  social/footer widgets, microsite ad blocks, and related-post lists from the
  text sent to entity recognition, while retaining article image captions.
- Origo article paragraphs can come from `.block-content`; selected sources
  fall back to `og:description` when the page exposes no article body, such as
  an age-confirmation screen or a video-only article.
- Handles `"key":"text"` / `"value"` script patterns.
- Uses HtmlAgilityPack `HtmlDocument`.

### Paginators & Link Parsers

- `ArchiveLinkParser.cs` — parses archive pages for article links.
- Each `*Source` creates its own parsers and paginator (the old factories were removed).
- `IContentParser`, `ILinkParser`, `IPaginator` implementations.

### Database (`Agitprop.Data/`)

| File | Purpose |
|------|---------|
| `AppDbContext.cs` | EF Core `DbContext` with `DbSet<PostgresArticle>`, `DbSet<PostgresEntity>`, `DbSet<PostgresMention>` |
| `NewsfeedDB.cs` | Implements `INewsfeedDB` — `CreateMentionsAsync`, `IsUrlAlreadyExists` |
| `EntityRepository.cs` | Implements `IEntityRepository` — entity browse/mentions/pagination |
| `TrendingRepository.cs` | Implements `ITrendingRepository` — trending entities |
| `Mappers.cs` | Maps DB models ↔ domain models |

`NewsfeedDB` treats a concurrent duplicate insert on the `IX_articles_Url`
constraint as an already-persisted article. Other database failures continue to
propagate for retry/error handling.

### Other Key Files

| File | Purpose |
|------|---------|
| `NewsfeedSink.cs` | Implements `ISink` — `EmitAsync` persists results; `CheckPageAlreadyVisited` |
| `Extensions.cs` | `AddNewsfeedSink` DI registration (also registers `ISink`) |
| `Helper.cs` | Utility helpers for scraping |
| `CommonArchiveSchemas.cs` | Archive page HTML structure schemas |

## Test Project (`Agitprop.Sinks.Newsfeed_Test`)

- `ContentParserOfflineTests.cs` and `ArchiveParserTests.cs` — deterministic checks against HTML fixtures under `TestData/`.
- `ContentParserOnlineTests.cs` — explicit, opt-in checks against live sites whose content can change.

`Agitprop.UnitTests` contains service-free tests for crawl orchestration, sink behavior, job
creation, and the NLP HTTP client. `Agitprop.IntegrationTests` uses a dedicated Aspire AppHost
to test the Web API against an isolated PostgreSQL instance; it requires Docker.

### Running tests:

```bash
# Deterministic tests used by CI
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj --configuration Release
dotnet test Agitprop.UnitTests/Agitprop.UnitTests.csproj --configuration Release

# Aspire API + PostgreSQL (requires Docker)
dotnet test Agitprop.IntegrationTests/Agitprop.IntegrationTests.csproj --configuration Release

# Online (explicit; accesses live sites)
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj \
  --filter "FullyQualifiedName~ContentParserOnlineTests"
```
