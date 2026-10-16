# Agitprop.Infrastructure

## Purpose

Implements the **core scraping infrastructure**: the Spider (crawler), static
HTTP page loader, proxy pool, rotating HTTP client pool, and DI registration for
non-browser scraping.

## Key Files / Classes

### Spider.cs

- Implements `ISpider` with `CrawlAsync(ScrapingJob job, ISink sink, CancellationToken)`.
- Uses `IPageTransport` to load pages. ActivitySource: `"Agitprop.Spider"`.
- **Two processing paths:**
  - `ProcessTargetPage`: when `PageCategory == TargetPage`, runs ContentParsers
    and calls `sink.EmitAsync()`.
  - `ProcessPage`: runs LinkParsers + pagination to discover new URLs.
- Tracks metrics: `pagesProcessed` counter, `processingTime` histogram.
- Checks `sink.CheckPageAlreadyVisited` before crawling.

### CookieStorage.cs

- Implements `ICookiesStorage` using an in-memory `CookieContainer`.
- `AddAsync(CookieContainer)` replaces the stored container.
- `GetAsync()` returns the current `CookieContainer`.

### Proxy Pool

- **ProxyDto.cs** — DTO model for proxy API responses (`ProxyDto`, `Proxy`,
  `IpData`).
- **ProxyPoolService.cs** (`ProxyPool`) — implements `IProxyPool`, returning
  proxies from its working set or fetching more from the configured providers
  when needed; callers report proxy success or failure.
- **RotatingHttpClientPool.cs** — manages a pool of `HttpClient` instances
  each bound to a different proxy. ActivitySource: `"Agitprop.RotatingHttpClientPool"`.

### PageLoader/

| File | Purpose |
|------|---------|
| `PageTransport.cs` | Combines loaders into `IPageTransport` |
| `HttpStaticPageLoader.cs` | Static HTTP page loading via `IStaticPageLoader` |
| `BrowserPageLoader.cs` | Browser-based loading |

### PageRequester/

| File | Purpose |
|------|---------|
| `RotatingProxyPageRequester.cs` | Uses proxy pool for requests |
| `RespectfulPageRequester.cs` | Direct HTTP with politeness delay |
| `PageRequester.cs` | Base requester |

### ProxyProviders/

| File | Purpose |
|------|---------|
| `IProxyProvider.cs` | Interface |
| `ProxyScrapeProxyProvider.cs` | ProxyScrape API provider |
| `RedScrapeProxyProvider.cs` | RedScrape API provider |

## DI Registration (Extensions.cs)

`ConfigureInfrastructureWithoutBrowser(services, useProxies)`:

- `ISpider` → `Spider`
- `ICookiesStorage` → `CookieStorage`
- `IStaticPageLoader` → `HttpStaticPageLoader`
- If `useProxies`:
  - `IProxyProvider` → `ProxyScrapeProxyProvider` + `RedScrapeProxyProvider`
  - `IProxyPool` → `ProxyPool`
  - `RotatingHttpClientPool`
  - `IPageRequester` → `RotatingProxyPageRequester`
- Else:
  - `IPageRequester` → `RespectfulPageRequester`
- `IPageTransport` (singleton factory) — combines `IStaticPageLoader` +
  `IBrowserPageLoader` + `ILogger`.

`ConfigureInfrastructureWithBrowser(builder, useProxies)` reuses this shared
registration and adds the browser loader selected for direct or proxy mode.
