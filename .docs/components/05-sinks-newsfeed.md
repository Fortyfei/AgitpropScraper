# Agitprop.Sinks.Newsfeed

## Purpose

The **sink layer** — contains site-specific content parsers, paginators,
link parsers, EF Core models, database context, and the `NewsfeedSink`
implementation that persists scraped data to PostgreSQL.

> Note: The project folder is named `Agitprop.Sinks.Newsfeed` (not
> `Agitprop.Scraper.Sinks.Newsfeed` as referenced in some older docs).

## Contents

### Content Parsers (`Scrapers/ContentParsers/`)

All extend the abstract `BaseArticleContentParser : IContentParser`.

| Parser | Source Site |
|--------|-------------|
| `BaseArticleContentParser.cs` | Base class — XPath fallback extraction |
| `MagyarJelenArticleContentParser.cs` | Magyar Jelen |
| `IndexArticleContentParser.cs` | Index |
| `HvgArticleContentParser.cs` | HVG |
| `HuszonnegyArticleContentParser.cs` | 24.hu |
| `AlfahirArticleContentParser.cs` | Alfahir |
| `MerceArticleContentParser.cs` | Mérce |
| `MandinerArticleContentParser.cs` | Mandiner |
| `MagyarNemzetArticleContentParser.cs` | Magyar Nemzet |
| `MetropolArticleContentParser.cs` | Metropol |
| `NegynegynegyArticleContentParser.cs` | 444.hu |
| `TelexArticleContentParser.cs` | Telex |
| `RtlArticleContentParser.cs` | RTL |
| `RipostArticleContentParser.cs` | Ripost |
| `PestisracokArticleContentParser.cs` | PestiSracok |
| `OrigoArticleContentParser.cs` | Origó |

#### BaseArticleContentParser

- Abstract properties: `DateXPaths`, `TitleXPaths`, `LeadXPaths`,
  `ArticleXPaths` (lists of XPath strings for fallback extraction).
- `SourceSite` (abstract `NewsSites`).
- Handles JSON-LD `articleBody` and `"key":"text"` / `"value"` script patterns.
- Uses HtmlAgilityPack `HtmlDocument`.

### Paginators & Link Parsers

- `ArchiveLinkParser.cs` — parses archive pages for article links.
- `ArchiveLinkParserFactory.cs` — factory for archive link parsers.
- `PaginatorFactory.cs` — factory for paginators.
- `IContentParser`, `ILinkParser`, `IPaginator` implementations.

### Database (`Database/`)

| File | Purpose |
|------|---------|
| `AppDbContext.cs` | EF Core `DbContext` with `DbSet<PostgresArticle>`, `DbSet<PostgresEntity>`, `DbSet<PostgresMention>` |
| `NewsfeedDB.cs` | Implements `INewsfeedDB` — `CreateMentionsAsync`, `IsUrlAlreadyExists` |
| `EntityRepository.cs` | Implements `IEntityRepository` — entity browse/mentions/pagination |
| `TrendingRepository.cs` | Implements `ITrendingRepository` — trending entities |
| `Mappers.cs` | Maps DB models ↔ domain models |

### Other Key Files

| File | Purpose |
|------|---------|
| `NewsfeedSink.cs` | Implements `ISink` — `EmitAsync` persists results; `CheckPageAlreadyVisited` |
| `Extensions.cs` | `AddNewsfeedSink` / `AddNewsfeedRepositories` DI registration |
| `Factories/ScrapingJobFactory.cs` | Builds `ScrapingJob` for article/archive sources |
| `Factories/ContentParserFactory.cs` | Maps `NewsSites` → `IContentParser` |
| `Consumer/NewsfeedJobConsumer.cs` | MassTransit consumer — orchestrates the crawl |
| `Consumer/NewsfeedJobConsumerDefinition.cs` | Consumer definition (queues, concurrency) |
| `Helper.cs` | Utility helpers for scraping |
| `CommonArchiveSchemas.cs` | Archive page HTML structure schemas |

## Test Project (`Agitprop.Sinks.Newsfeed_Test`)

- `ContentParserOfflineTests/` — tests parsers against fixture HTML snapshots.
- `ContentParserOnlineTests/` — tests parsers against live sites.

### Running tests:

```bash
# Offline (fixture-based)
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj \
  --filter "FullyQualifiedName~ContentParserOfflineTests"

# Online (live sites)
dotnet test Agitprop.Sinks.Newsfeed_Test/Agitprop.Sinks.Newsfeed_Test.csproj \
  --filter "FullyQualifiedName~ContentParserOnlineTests"
```
