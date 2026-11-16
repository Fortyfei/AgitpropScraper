# Agitprop.Web

## Purpose

A **Blazor Web App** dashboard that visualizes entity analytics and trending data.
It also owns the analytics queries: instead of calling a separate Web API, the
`AnalyticsService` reads straight from the repositories in `Agitprop.Data`, so
there is no HTTP hop, no controller layer, and no DTO boundary.

## Key Features

- Razor Components with Interactive Server rendering only.
- ApexCharts integration through the `Blazor-ApexCharts` package.
- `AnalyticsService` queries the repositories in-process, with an `IMemoryCache`
  in front of the list queries.
- OTLP metrics export for telemetry (`Agitprop.Web` meter).
- EF Core migrations applied at startup when configured.
- Responsive coverage-monitor navigation: a top bar on desktop and a fixed
  bottom tab bar on narrow screens.
- Dashboard and entity timeline charts, a top-sources donut chart, and an
  entity mention-count bar chart.
- Entity detail pages include ranked co-mentions; the Sources page includes a
  ranking; the Sources page includes a publisher-by-entity coverage matrix.
- The Timeline page (`/timeline`) shows a clickable articles-per-day strip with
  spike days highlighted above a day-grouped article feed. The feed filters by
  publisher, entity and headline search; clicking a publisher or entity on an
  article applies that filter.

## Rendering Mode

`Program.cs` registers `AddRazorComponents().AddInteractiveServerComponents()`.
`App.razor` applies Interactive Server rendering to `HeadOutlet` and `Routes`,
and every page declares `@rendermode InteractiveServer`. There is no
WebAssembly render mode and no separate WASM project.

## Query layer

| Route | Page | Service call |
|-------|------|--------------|
| `/` and `/home` | `Home.razor` | `GetTopMentionedEntitiesAsync`, `GetEntitiesTimelineAsync` |
| `/entities` | `EntityBrowser.razor` | `GetEntitiesBrowseAsync` |
| `/entity/{Id}` | `EntityDetail.razor` | `GetEntityByIdAsync`, `GetEntitiesTimelineAsync`, `GetEntityArticlesAsync`, `GetEntityDomainStatsAsync`, `GetCoMentionsAsync` |
| `/sources` | `Sources.razor` | `GetSourcesAsync`, `GetSourceEntityCoverageAsync` |
| `/timeline` | `Timeline.razor` | `GetArticleFeedAsync`, `GetDailyArticleCountsAsync` |

`AnalyticsService` is registered as a **transient** service. Each call opens its
own DI scope with `IServiceScopeFactory.CreateScope()`, so the scoped
`AppDbContext` lives exactly as long as one query — a Blazor Server circuit can
stay open for hours and must not share a single EF Core context.

Invalid arguments (reversed date ranges, oversized windows, malformed entity
ids) throw `ArgumentException`. Storage failures are logged and reported as a
null result, so pages keep rendering their existing "unable to load" state
instead of faulting the circuit.

Cache durations: 15 minutes for list queries, 1 hour for entity details,
5 minutes for daily counts.

## Reporting calendar

Instants are stored and compared in UTC, but every day bucket, date-range filter
and "today" default uses `ReportingTimeZone` (`Agitprop.Core`, `Europe/Budapest`)
— the calendar the outlets publish in and the audience reads. An article stored
at `2026-02-10T23:30Z` therefore appears in the 11th's bucket and in the
`2026-02-11..2026-02-11` feed range.

## Tracing: one browser session is one trace

Blazor Server dispatches each interaction on its own async flow with no ambient
span, so without help every query would be exported as a separate trace. Two
pieces fix that:

1. **`CircuitTraceContext`** (scoped, so one per circuit) captures the span of the
   page load that opened the circuit. `TraceOriginCapture` in `MainLayout.razor`
   takes it during prerender (inside the initial HTTP request) and persists it
   through `PersistentComponentState`, so the interactive circuit can restore the
   same W3C `traceparent` after hydration.
2. **`UserJourneyFactory`** starts each interaction's journey as a child of that
   captured span, so the whole session continues one trace.

The result: a browser session that visits `/`, then `/entities`, then `/timeline`
produces **one** trace whose root is the first page load:

```
GET /                      page load that opened the circuit
├─ home.load
│  ├─ analytics.top_mentions
│  └─ entities.timeline
├─ entities.load           later click, still the same trace
│  └─ entities.browse
└─ timeline.load           another click
   ├─ articles.feed
   └─ articles.daily
```

Each query span (`Agitprop.Web.AnalyticsService`) is a child of the active
journey. A second browser (a second circuit) gets its own origin, so sessions do
not mix: landing directly on `/sources` starts a trace rooted at that
`GET /sources`.

| Page | Journey names |
|------|---------------|
| `Home.razor` | `home.load` |
| `EntityBrowser.razor` | `entities.load` |
| `EntityDetail.razor` | `entity.view`, `entity.data`, `entity.more-articles` |
| `Sources.razor` | `sources.load` |
| `Timeline.razor` | `timeline.load`, `timeline.more-articles` |

`Agitprop.ServiceDefaults` keeps that view clean by turning off Blazor's own
`Route`/`Event` activities and the SignalR `ComponentHub` activities, and by not
tracing static files (`/_blazor`, `/_framework/`, `/_content/`, `/lib/`, and
`*.css`/`*.js`/`*.map`/`*.png`/`*.ico`). Those arrive as separate messages and
would otherwise appear as unrelated single-span traces.

Note that a long session yields a long trace: the `GET /` span ends when the
response is sent, while later journeys start minutes later under it. If a
dashboard rejects spans that outlive their parent, group by the journey span or
cap the session with a new `traceparent` instead.

## Project Structure

| File | Purpose |
|------|---------|
| `Program.cs` | Service defaults, render modes, ApexCharts, memory cache, repositories, migrations. |
| `Components/App.razor` | Root component and document shell. |
| `Components/Layout/MainLayout.razor` | Shared navigation and page-content layout. |
| `Components/Pages/` | Dashboard, entities, entity detail, sources, and timeline pages. |
| `Services/AnalyticsService.cs` | Scope-per-call analytics queries over the repositories. |
| `Services/WebMetrics.cs` | `Agitprop.Web` meter with the `query.cache.requests` counter. |
| `Diagnostics/UserJourney.cs` | `Agitprop.Web.Journey` span that groups one interaction's queries into the session trace. |
| `Diagnostics/CircuitTraceContext.cs` | Per-circuit origin: the page-load span every later journey continues. |
| `Diagnostics/UserJourneyFactory.cs` | Creates journeys parented to the circuit origin. |
| `Components/TraceOriginCapture.razor` | Captures the page-load trace during prerender and restores it after hydration. |
| `Models/AnalyticsModels.cs` | View models returned by `AnalyticsService`. |

## Configuration

- `ConnectionStrings__newsfeed` — injected by the AppHost (`WithReference(newsfeedDb)`).
- `ApplyMigrationsAtStartup=true` runs the EF Core migrations on boot; Development
  always applies them.
- `appsettings.json` + `appsettings.Development.json`.

## Startup

Started by `Agitprop.AppHost` as project `web`:

```csharp
builder.AddProject<Projects.Agitprop_Web>("web")
    .WaitFor(newsfeedDb)
    .WithReference(newsfeedDb)
    .WithExternalHttpEndpoints();
```

## Package Requirements

- `Blazor-ApexCharts` provides the chart components used by the pages.
