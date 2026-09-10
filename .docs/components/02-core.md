# Agitprop.Core

## Purpose

Shared library of **core abstractions, models, enums, and interfaces** used by all
other projects in the solution. This is a pure class library with no external
dependencies beyond the .NET runtime.

## Contents

### Models

- `Entity.cs` — `Entity` record: `Id`, `Name`, `Type`.
- `Article.cs` — `Article` record: `Id`, `Title`, `Url`, `PublishedTime`,
  `MentionedEntities` (list of `Entity`).

### Enums (`Agitprop.Core/Enums/`)

- `PageCategory` — e.g. `TargetPage`, `Archive`, etc.
- `PageType` — page classification enum.
- `PageContentType` — content type enum.
- `NewsSites` — supported news sites (MagyarJelen, Index, Hvg, ...).
- `PageActionType` — browser action types.

### Key Records

- `ScrapingJob` — record with `Url`, `PageCategory`, `PageType`, `Actions`
  (list of `PageAction`), `ContentParsers` (list of `IContentParser`),
  `LinkParsers` (list of `ILinkParser`), `Pagination` (`IPaginator`).
- `ScrapingJobDescription` — `required string Url`.
- `ContentParserResult` — record class: `NewsSites SourceSite`,
  `DateTime PublishDate` (normalized to UTC), `string Text`, `string Title = ""`.
- `PageAction` — record with `PageActionType` + `object[] Parameters`.
- `NamedEntity` — `(string Name, string Type)` JSON-named tuple; implements
  `IEquatable<NamedEntity>`.
- `NamedEntityCollection` — record with `List<NamedEntity> Entities` and
  computed properties: `PER`, `LOC`, `ORG`, `MISC`, `All`.

### Interfaces (`Agitprop.Core/Interfaces/`)

| Interface | Key Methods |
|-----------|-------------|
| `ISpider` | `CrawlAsync(ScrapingJob, ISink, CancellationToken)` |
| `IProxyPool` | `GetNextProxyAsync`, `MarkDeadAsync`, `MarkSuccessAsync` (IAsyncDisposable) |
| `IPageTransport` | `LoadAsync(string, PageLoadOptions?, CancellationToken)`; records `PageLoadOptions`, `PageLoadResult`; `TransportMode` enum |
| `ISink` | `EmitAsync(string, List<ContentParserResult>, CancellationToken)`, `CheckPageAlreadyVisited(string)` |
| `IContentParser` | `ParseContentAsync(HtmlDocument)`, `ParseContentAsync(string)` |
| `ILinkParser` | `GetLinksAsync(string, string)`, `GetLinksAsync(string, HtmlDocument)` |
| `IPaginator` | `GetNextPageAsync(string, string)` |
| `INamedEntityRecognizer` | `AnalyzeSingleAsync(string)`, `AnalyzeBatchAsync(string[])` |
| `INewsfeedDB` | `CreateMentionsAsync(string, ContentParserResult, NamedEntityCollection)`, `IsUrlAlreadyExists(string)` |
| `IEntityRepository` | `GetAllEntitiesAsync`, `GetMentioningArticlesAsync`, `GetEntityByIdAsync`, `SearchEntitiesAsync`, etc. |
| `ITrendingRepository` | `GetTrendingEntitiesAsync(DateOnly, DateOnly, int)` |
| `IStaticPageLoader` | `Load(string)` |
| `IBrowserPageLoader` | `Load(string, object?, bool)` |
| `ICookiesStorage` | `AddAsync(CookieContainer)`, `GetAsync()` |
| `IPageRequester` | `GetAsync(string)`, `CookieContainer` property |
| `IBrowserAction` | `ExecuteAsync(IPage)` |
| `BaseEntityRepository` | Base repository for entity queries |
