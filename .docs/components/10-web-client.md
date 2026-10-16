# Agitprop.Web.Client

## Purpose

A **Blazor Web App** dashboard that consumes the Web API to visualize entity
analytics and trending data. It supports Interactive Server and Interactive
WebAssembly render modes.

## Key Features

- Razor Components with server-side interactivity.
- ApexCharts integration (`Agitprop.Web.Client` depends on
  `Blazored.ApexCharts` / `Blazor-ApexCharts`).
- HTTP client for API calls (`AnalyticsService`).
- OTLP metrics export for telemetry.

## Rendering Mode

The host registers both interactive render modes. `App.razor` selects the mode
for its routed components.

## Project Structure

| File | Purpose |
|------|---------|
| `Program.cs` | Service collection setup: AddServiceDefaults, AddRazorComponents, AddApexCharts, AddHttpClient |
| `Components/App.razor` | Root component with `<Router>`, `<HeadOutlet>`, `<Routes>` |
| Services | `AnalyticsService` for API calls |

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

- `Microsoft.AspNetCore.Components.WebAssembly.Server` supports WebAssembly
  rendering from the Blazor Web App host.
