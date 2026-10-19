using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using Microsoft.Extensions.Logging;

using PuppeteerSharp;

namespace Agitprop.Infrastructure.PageLoader;

/// <summary>
/// An abstract base class for loading web pages using a browser.
/// </summary>
public abstract class BrowserPageLoader
{
    /// <summary>
    /// A dictionary mapping page action types to their corresponding execution logic.
    /// </summary>
    protected readonly Dictionary<PageActionType, Func<IPage, object[], CancellationToken, Task>> PageActions = new()
    {
        {
            PageActionType.ScrollToEnd,
            async (page, _, cancellationToken) =>
                await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);")
                    .WaitAsync(cancellationToken)
        },
        { PageActionType.Wait, async (_, data, cancellationToken) => await Task.Delay(Convert.ToInt32(data.First()), cancellationToken) },
        {
            PageActionType.WaitForNetworkIdle,
            async (page, _, cancellationToken) => await page.WaitForNetworkIdleAsync().WaitAsync(cancellationToken)
        },
        { PageActionType.Click, async (page, data, cancellationToken) => await page.ClickAsync((string)data.First()).WaitAsync(cancellationToken) },
        {
            PageActionType.Execute,
            async (page, action, cancellationToken) =>
                await ((IBrowserAction)action.First()).ExecuteAsync(page).WaitAsync(cancellationToken)
        },
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserPageLoader"/> class.
    /// </summary>
    /// <param name="logger">The logger for logging information and errors.</param>
    protected BrowserPageLoader(ILogger<BrowserPageLoader>? logger = default)
    {
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger for logging information and errors.
    /// </summary>
    protected ILogger<BrowserPageLoader>? Logger { get; }
}
