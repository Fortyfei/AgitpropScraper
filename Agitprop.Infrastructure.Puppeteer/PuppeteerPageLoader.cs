using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Diagnostics;

using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
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
    private string? _executablePath;

    public PuppeteerPageLoader(
        ICookiesStorage cookiesStorage,
        ILogger<PuppeteerPageLoader>? logger = default,
        IConfiguration? configuration = null)
        : base(logger)
    {
        _cookiesStorage = cookiesStorage;
        _retryCount = configuration?.GetValue<int>("Retry:PuppeteerPageLoader", 3) ?? 3;
    }

    public async Task<string> Load(
        string url,
        List<PageAction>? pageActions = null,
        bool headless = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var trace = ActivitySource.StartActivity("LoadPageWithBrowser", ActivityKind.Internal);
        trace?.SetTag("server.address", TelemetryUrl.GetServerAddress(url));
        Logger?.LogDebug("Starting page load: {Url}", TelemetryUrl.RedactQueryAndFragment(url));

        var executablePath = await EnsureBrowserAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Logger?.LogDebug("Launching browser");
        await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = headless,
            ExecutablePath = executablePath,
        });

        cancellationToken.ThrowIfCancellationRequested();
        Logger?.LogDebug("Creating a new page");
        await using var page = (await browser.PagesAsync().WaitAsync(cancellationToken))[0];

        var cookies = await _cookiesStorage.GetAsync().WaitAsync(cancellationToken);
        if (cookies != null)
        {
            var cookieParams = cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value
            }).ToArray();

            await page.SetCookieAsync(cookieParams).WaitAsync(cancellationToken);
        }

        Logger?.LogDebug("Navigating to page: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        await RetryPolicyFactory.CreateExceptionPolicy(
                _retryCount,
                (ex, attempt) =>
                {
                    Logger?.LogWarning(ex, "[RETRY] Exception navigating to page {Url} on attempt {Attempt}",
                        TelemetryUrl.RedactQueryAndFragment(url), attempt);
                })
            .ExecuteAsync(
                    ct => page.GoToAsync(url, WaitUntilNavigation.Networkidle2).WaitAsync(ct),
                cancellationToken);

        if (pageActions != null)
        {
            Logger?.LogDebug("Performing page actions ({Count})", pageActions.Count);
            for (int i = 0; i < pageActions.Count; i++)
            {
                var pageAction = pageActions[i];
                Logger?.LogDebug("Executing page action {Current}/{Total} of type {Type}",
                    i + 1,
                    pageActions.Count,
                    pageAction.Type);

                await PageActions[pageAction.Type](page, pageAction.Parameters, cancellationToken);
            }
        }

        var html = await page.GetContentAsync().WaitAsync(cancellationToken);
        Logger?.LogDebug("Page load completed: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        return html;
    }

    public Task<string> Load(
        string url,
        object? pageActions,
        bool headless,
        CancellationToken cancellationToken = default)
        => Load(url, (List<PageAction>?)pageActions, headless, cancellationToken);

    /// <summary>
    /// Downloads the browser on first use and reuses the executable afterwards; downloading it on
    /// every page load costs seconds and a network round trip per page.
    /// </summary>
    private async Task<string> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_executablePath is not null)
        {
            return _executablePath;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_executablePath is not null)
            {
                return _executablePath;
            }

            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty
            });

            Logger?.LogInformation("Downloading browser...");
            await RetryPolicyFactory.CreateExceptionPolicy(
                    _retryCount,
                    (ex, attempt) =>
                    {
                        Logger?.LogWarning(ex, "[RETRY] Exception downloading browser on attempt {attempt}", attempt);
                    })
                .ExecuteAsync(
                    _ => browserFetcher.DownloadAsync(BrowserTag.Stable),
                    cancellationToken);

            _executablePath = browserFetcher.GetInstalledBrowsers().First().GetExecutablePath();
            Logger?.LogInformation("Browser downloaded successfully");

            return _executablePath;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
