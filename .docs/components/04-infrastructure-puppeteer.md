# Agitprop.Infrastructure.Puppeteer

## Purpose

Adds **PuppeteerSharp-based browser automation** to the scraping pipeline. Extends
`Agitprop.Infrastructure` with a browser page loader that supports optional
proxy rotation.

## Key File: Extensions.cs

`ConfigureInfrastructureWithBrowser(builder, useProxies)`:

1. Calls `ConfigureInfrastructureWithoutBrowser(services, useProxies)` from
   `Agitprop.Infrastructure` to register all non-browser services.
2. Registers `IBrowserPageLoader`:
   - If `useProxies`: → `PuppeteerPageLoaderWithProxies`
   - Else: → `PuppeteerPageLoader`

## Key Files

| File | Purpose |
|------|---------|
| `PuppeteerPageLoader.cs` | Loads pages using PuppeteerSharp browser. Implements `IBrowserPageLoader.Load(url, pageActions, headless)`. |
| `PuppeteerPageLoaderWithProxies.cs` | Same as above but routes browser traffic through rotating proxies. |

## Usage

Registered in `Agitprop.Scraper.Consumer/Program.cs`:

```csharp
builder.ConfigureInfrastructureWithBrowser(false); // or true for proxies
```

This is used by the **Consumer** to enable JavaScript rendering for sites that
require it (`PageLoadOptions.RequiresJavaScript = true`).
