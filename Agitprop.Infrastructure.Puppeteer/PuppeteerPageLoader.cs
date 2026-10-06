using Polly;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Diagnostics;

using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.PageLoader;

using Microsoft.Extensions.Logging;

using PuppeteerSharp;

namespace Agitprop.Infrastructure.Puppeteer;

/// <summary>
/// A Puppeteer-based page loader for loading web pages and performing actions on them.
/// </summary>
public class PuppeteerPageLoader : BrowserPageLoader, IBrowserPageLoader
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _retryCount;
    private readonly ActivitySource ActivitySource = new("Agitprop.PageLoader.PuppeteerPageLoader");

    public PuppeteerPageLoader(
        ICookiesStorage cookiesStorage,
        ILogger<PuppeteerPageLoader>? logger = default,
        IConfiguration? configuration = null)
        : base(logger)
    {
        _cookiesStorage = cookiesStorage;
        _retryCount = configuration?.GetValue<int>("Retry:PuppeteerPageLoader", 3) ?? 3;
    }

    public async Task<string> Load(string url, List<PageAction>? pageActions = null, bool headless = true)
    {
        using var trace = ActivitySource.StartActivity("LoadPageWithBrowser", ActivityKind.Producer);
        trace?.SetTag("url", TelemetryUrl.RedactQueryAndFragment(url));
        Logger?.LogDebug("Starting page load: {Url}", TelemetryUrl.RedactQueryAndFragment(url));

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        });

        await _semaphore.WaitAsync();
        try
        {
            Logger?.LogInformation("Downloading browser...");
            await Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    _retryCount,
                    attempt => TimeSpan.FromSeconds(0.5 * attempt),
                    (ex, ts, attempt, ctx) =>
                    {
                        Logger?.LogWarning(ex, "[RETRY] Exception downloading browser on attempt {attempt}", attempt);
                    })
                .ExecuteAsync(() => browserFetcher.DownloadAsync(BrowserTag.Stable));
            Logger?.LogInformation("Browser downloaded successfully");
        }
        finally
        {
            _semaphore.Release();
        }

        Logger?.LogInformation("Launching browser");
        await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = headless,
            ExecutablePath = browserFetcher.GetInstalledBrowsers().First().GetExecutablePath(),
        });

        Logger?.LogInformation("Creating a new page");
        await using var page = (await browser.PagesAsync())[0];

        var cookies = await _cookiesStorage.GetAsync();
        if (cookies != null)
        {
            var cookieParams = cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value
            }).ToArray();

            await page.SetCookieAsync(cookieParams);
        }

        Logger?.LogDebug("Navigating to page: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        await Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                _retryCount,
                attempt => TimeSpan.FromSeconds(0.5 * attempt),
                (ex, ts, attempt, ctx) =>
                {
                    Logger?.LogWarning(ex, "[RETRY] Exception navigating to page {Url} on attempt {Attempt}",
                        TelemetryUrl.RedactQueryAndFragment(url), attempt);
                })
            .ExecuteAsync(() => page.GoToAsync(url, WaitUntilNavigation.Networkidle2));

        if (pageActions != null)
        {
            Logger?.LogInformation("Performing page actions ({count})", pageActions.Count);
            for (int i = 0; i < pageActions.Count; i++)
            {
                var pageAction = pageActions[i];
                Logger?.LogDebug("Executing page action {Current}/{Total} of type {Type}",
                    i + 1,
                    pageActions.Count,
                    pageAction.Type);

                await PageActions[pageAction.Type](page, pageAction.Parameters);
            }
        }

        var html = await page.GetContentAsync();
        Logger?.LogInformation("Page load completed: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        return html;
    }

    public Task<string> Load(string url, object? pageActions, bool headless)
        => Load(url, (List<PageAction>?)pageActions, headless);
}
