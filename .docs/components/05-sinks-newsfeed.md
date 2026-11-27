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
- Publication times are normalised to UTC in `TryParseDateString`. Values that
  state an offset (`Z`, `+01:00`, `+0100`) are honoured as written; offset-less
  values are read as Budapest wall-clock time through `ReportingTimeZone`
  (`Europe/Budapest`) and converted to UTC, including the DST gap and overlap.
  The host machine's timezone never influences the result, so a UTC container
  stores the same instant as a developer machine in Budapest.
- Index uses its `i:publication` date metadata when `article:published_time`
  is unavailable; the bare Hungarian date is read as Budapest midnight, which
  lands on the previous day in UTC.
- Day buckets and date-range filters use the same calendar: repositories turn an
  inclusive `DateOnly` range into UTC bounds with `ReportingTimeZone.ToUtcRange`
  and group by `ReportingTimeZone.ToDay`, so an article published at 23:30 UTC
  belongs to the next reporting day.
- The Index parser excludes scripts/styles, the Google preference banner,
  social/footer widgets, microsite ad blocks, and related-post lists from the
  text sent to entity recognition, while retaining article image captions.
- The Blikk parser likewise keeps the Indamedia cross-brand teaser slider
  ("Olvasta már?"), the promotional article link, and the tag-recommendation
  strip out of the entity-recognition input. The slider labels unrelated
  Index articles with an "Index" badge, so without this filter most Blikk
  articles appeared to mention the Index outlet.
- The 24.hu parser keeps list items (the daily digest posts are a `<ul>` of
  headlines) and drops the "Cikkajánló" widget ("Kapcsolódó" plus another
  article's title and lead) and the sponsored box, while keeping the
  editorial "Keretes megjelenés" box. It no longer unions the body with a
  positional container selector, which used to append a second copy of every
  article body.
- The Mérce parser drops the end-of-article donation box ("Csatlakozom!") but
  keeps the "Kiemelt kép" attachment credit.
- A body whose matched nodes carry no text at all is reported as a parse
  failure instead of being persisted as a title-only article.
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
| `AppDbContext.cs` | EF Core `DbContext` with `DbSet<PostgresArticle>`, `DbSet<PostgresEntity>`, `DbSet<PostgresEntityAlias>`, `DbSet<PostgresMention>` |
| `NewsfeedDB.cs` | Implements `INewsfeedDB` — `CreateMentionsAsync`, `IsUrlAlreadyExists` |
| `EntityNormalizer.cs` | Comparison key for surface forms (case, whitespace, edge punctuation; accents preserved) |
| `EntityCatalog.cs` | Curated canonical entities with fixed types and inflected/press aliases; validates on first use |
| `EntityCatalogSeeder.cs` | Idempotent startup seed of the catalog; folds an observed entity whose whole name is a catalog alias |
| `EntityResolver.cs` | Pure resolution: exact name → typed alias → single cross-type candidate → new entity; rejects surface noise |
| `EntityRepository.cs` | Implements `IEntityRepository` — entity browse/mentions/pagination; search covers names and aliases |
| `TrendingRepository.cs` | Implements `ITrendingRepository` — trending entities counted inside the requested window |
| `Mappers.cs` | Maps DB models ↔ domain models |

Entity identity is the entity `Id`; `Name` is only the display form. Uniqueness is
`(NormalizedName, Type)`, and `entity_aliases` stores `(NormalizedAlias, Type)`
pairs. The type is part of the key on purpose: the river `Tisza` (`LOC`) and the
party `Tisza Párt` (`ORG`) resolve to different rows, while both are matched by
the same `Tisza` surface form through their typed aliases. A type mismatch with a
single candidate (the model occasionally swaps `LOC`/`ORG`) still resolves and is
counted as `newsfeed.entities.resolved{outcome=type_conflict}`. Mentions keep the
raw `SurfaceForm`, and two surface forms resolving to one entity inside one
article produce a single mention.

`NewsfeedDB` treats a concurrent duplicate insert on the `IX_articles_Url`
constraint (or on the mention primary key) as an already-persisted article.
Entity and alias creation uses `INSERT ... ON CONFLICT`, so parallel workers do
not race on the unique keys. Other database failures continue to
propagate for retry/error handling.

The context is registered through Aspire with `EnableRetryOnFailure`, so it runs
a retrying execution strategy. That strategy rejects user-initiated transactions:
every unit of work that opens a transaction (`NewsfeedDB.CreateMentionsAsync`, the
`EntityCatalogSeeder` seed) must create it inside
`Database.CreateExecutionStrategy().ExecuteAsync(...)`, otherwise EF throws
"does not support user-initiated transactions" at the first query.

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
creation, and the dashboard query service. `Agitprop.IntegrationTests` uses a dedicated Aspire AppHost
to test the analytics queries against an isolated PostgreSQL instance; it requires Docker.

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
