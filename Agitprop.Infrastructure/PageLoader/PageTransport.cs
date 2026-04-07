using System.Diagnostics;
using Agitprop.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure.PageLoader;

public class PageTransport : IPageTransport
{
    private readonly IStaticPageLoader _staticPageLoader;
    private readonly IBrowserPageLoader? _browserPageLoader;
    private readonly ILogger<PageTransport> _logger;

    public PageTransport(
        IStaticPageLoader staticPageLoader,
        IBrowserPageLoader? browserPageLoader,
        ILogger<PageTransport>? logger = default)
    {
        _staticPageLoader = staticPageLoader;
        _browserPageLoader = browserPageLoader;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PageTransport>.Instance;
    }

    public async Task<PageLoadResult> LoadAsync(string url, PageLoadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new PageLoadOptions();
        var stopwatch = Stopwatch.StartNew();

        string content;
        if (options.RequiresJavaScript)
        {
            if (_browserPageLoader == null)
            {
                throw new InvalidOperationException("Browser loading is not available in this configuration.");
            }

            content = await _browserPageLoader.Load(url, options.Actions, options.Headless);
        }
        else
        {
            content = await _staticPageLoader.Load(url);
        }

        stopwatch.Stop();

        return new PageLoadResult(
            Content: content,
            Url: new Uri(url),
            StrategyName: "",
            Duration: stopwatch.Elapsed);
    }
}
