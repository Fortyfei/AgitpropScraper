
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Diagnostics;
using Agitprop.Core;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Microsoft.Extensions.Logging;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;

internal class PuppeteerPageLoaderWithProxies : BrowserPageLoader, IBrowserPageLoader
{
    private readonly int _retryCount;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ActivitySource ActivitySource = new("Agitprop.PageLoader.PuppeteerPageLoaderWithProxies");
    private string? _executablePath;

    private readonly IProxyPool _proxyPool;
    private readonly ICookiesStorage _cookieStorage;

    public PuppeteerPageLoaderWithProxies(
        ILogger<PuppeteerPageLoaderWithProxies> logger,
        IProxyPool proxyPool,
        ICookiesStorage cookieStorage,
        IConfiguration? configuration = null)
        : base(logger)
    {
        _proxyPool = proxyPool;
        _cookieStorage = cookieStorage;
        _retryCount = configuration?.GetValue<int>("Retry:PuppeteerPageLoaderWithProxies", 3) ?? 3;
    }

    public Task<string> Load(
        string url,
        object? pageActions,
        bool headless,
        CancellationToken cancellationToken = default)
        => Load(url, (List<PageAction>?)pageActions, headless, cancellationToken);

    public async Task<string> Load(
        string url,
        List<PageAction>? pageActions = null,
        bool headless = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var trace = ActivitySource.StartActivity("LoadPageWithProxyBrowser", ActivityKind.Internal);
        trace?.SetTag("url", TelemetryUrl.RedactQueryAndFragment(url));
        Logger?.LogDebug("Starting page load: {Url}", TelemetryUrl.RedactQueryAndFragment(url));

        if (_executablePath == null)
        {
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            {
                Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            });

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                Logger?.LogInformation("Downloading browser...");
                await RetryPolicyFactory.CreateExceptionPolicy(
                        _retryCount,
                        (ex, attempt) =>
                        {
                            Logger?.LogWarning(ex, "[RETRY] Exception downloading browser on attempt {attempt}", attempt);
                        })
                    .ExecuteAsync(
                        _ => browserFetcher.DownloadAsync(),
                        cancellationToken);
                _executablePath = browserFetcher.GetInstalledBrowsers().First().GetExecutablePath();
                Logger?.LogInformation("Browser downloaded successfully");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // ✅ NEW: Get proxy address from IProxyPool
        var proxyAddress = await _proxyPool.GetNextProxyAsync(cancellationToken);
        if (string.IsNullOrEmpty(proxyAddress))
            throw new InvalidOperationException("No proxy address available");

        string proxyArg = $"--proxy-server={proxyAddress}";
        trace?.SetTag("proxy", proxyAddress);
        Logger?.LogInformation("Launching Puppeteer with proxy: {proxy}", proxyAddress);


        var extra = new PuppeteerExtra();
        extra.Use(new StealthPlugin());

        var launchArgs = new List<string>
        {
            "--disable-infobars",
            "--ignore-certificate-errors",
            "--disable-dev-shm-usage",
            "--no-sandbox",
            "--disable-setuid-sandbox"
        };

        if (proxyArg != null)
            launchArgs.Add(proxyArg);

        await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = headless,
            ExecutablePath = _executablePath,
            Args = launchArgs.ToArray()
        });

        cancellationToken.ThrowIfCancellationRequested();
        await using var page = (await browser.PagesAsync().WaitAsync(cancellationToken))[0];

        var cookies = await _cookieStorage.GetAsync().WaitAsync(cancellationToken);
        if (cookies != null)
        {
            var cookieParams = cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Secure = c.Secure
            }).ToArray();

            await page.SetCookieAsync(cookieParams).WaitAsync(cancellationToken);
        }

        try
        {
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
            Logger?.LogDebug("Page navigation successful: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to navigate to page: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
            trace?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        if (pageActions != null)
        {
            Logger?.LogInformation("Performing {count} page actions", pageActions.Count);
            for (int i = 0; i < pageActions.Count; i++)
            {
                var pageAction = pageActions[i];
                Logger?.LogDebug("Executing page action {Current}/{Total} type {ActionType}",
                    i + 1, pageActions.Count, pageAction.Type);
                await PageActions[pageAction.Type](page, pageAction.Parameters, cancellationToken);
            }
        }

        var html = await page.GetContentAsync().WaitAsync(cancellationToken);
        Logger?.LogInformation("Page load completed: {Url}", TelemetryUrl.RedactQueryAndFragment(url));
        return html;
    }
}
