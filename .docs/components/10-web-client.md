# Agitprop.Web.Client

## Purpose

A Blazor Web App that consumes the Web API to visualize entity analytics,
trending data, and publisher coverage. The current routed pages use
`InteractiveServer` rendering.

## Key Features

- Razor Components with server-side interactivity.
- ApexCharts integration through the `Blazor-ApexCharts` package.
- `AnalyticsService` HTTP client for API calls.
- OTLP metrics export for telemetry.
- Responsive coverage-monitor navigation: a top bar on desktop and a fixed
  bottom tab bar on narrow screens.
- Dashboard and entity timeline charts, a top-sources donut chart, and an
  entity mention-count bar chart.

## Rendering Mode

`Program.cs` registers both Interactive Server and Interactive WebAssembly
render modes, but the current page components explicitly use
`InteractiveServer`. `App.razor` applies Interactive Server rendering to
`HeadOutlet` and `Routes`.

## Pages and visualization

| Route | Component | Behavior |
|-------|-----------|----------|
| `/` and `/home` | `Components/Pages/Home.razor` | Coverage dashboard with a timeline chart and ranked entities. |
| `/entities` | `Components/Pages/EntityBrowser.razor` | Searchable, paginated entity directory; a horizontal bar chart shows up to eight entities from the current results page. |
| `/entity/{Id}` | `Components/Pages/EntityDetail.razor` | Entity details, source breakdown, article list, and timeline chart. |
| `/sources` | `Components/Pages/Sources.razor` | Publisher coverage by normalized domain; a donut chart displays the top six sources and combines the remainder as “Other.” |

Timeframe changes reload the corresponding analytics data. The Sources chart
is shown only when source data is available; the Entities chart uses the
currently loaded results and updates with search, timeframe, and pagination.

## Layout and navigation

`Components/Layout/MainLayout.razor` provides the shared shell and page content
container. `NavMenu.razor` renders the Dashboard, Entities, and Sources links.
The desktop links are presented in the dark top bar with a visible active state.
At widths up to `38rem`, the navigation becomes a fixed bottom tab bar, includes
safe-area padding, and the content container reserves space below it.
`NavMenu.razor.css` and `MainLayout.razor.css` define these responsive styles.

## Project Structure

| File | Purpose |
|------|---------|
| `Program.cs` | Service setup, render modes, ApexCharts, and the `AnalyticsService` HTTP client. |
| `Components/App.razor` | Root component and document shell. |
| `Components/Layout/MainLayout.razor` | Shared navigation and page-content layout. |
| `Components/Pages/` | Dashboard, entities, entity detail, and sources pages. |
| `Services/AnalyticsService.cs` | Typed client-side access to analytics API endpoints. |

## Configuration

- References backend Web API via service discovery (Aspire).
- `appsettings.json` + `appsettings.Development.json`.

## Startup

Started by `Agitprop.AppHost` as project `frontend`:

```csharp
var frontend = builder.AddProject<Projects.Agitprop_Web_Client>("frontend")
    .WithReference(backend).WaitFor(backend)
    .WithExternalHttpEndpoints();
```

## Package Requirements

- `Blazor-ApexCharts` provides the chart components used by the pages.
- `Microsoft.AspNetCore.Components.WebAssembly.Server` supports the registered
  Interactive WebAssembly render mode; the current routed pages use
  Interactive Server.
