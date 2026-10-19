namespace Agitprop.Core.Interfaces;

/// <summary>
/// Defines the contract for loading web pages using a browser.
/// </summary>
public interface IBrowserPageLoader
{
    /// <summary>
    /// Loads a web page using a browser with optional page actions and headless mode.
    /// </summary>
    /// <param name="url">The URL of the page to load.</param>
    /// <param name="pageActions">Optional actions to perform on the page.</param>
    /// <param name="headless">Indicates whether the browser should run in headless mode.</param>
    /// <param name="cancellationToken">A token used to cancel page loading and actions.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the page content as a string.</returns>
    Task<string> Load(
        string url,
        object? pageActions,
        bool headless,
        CancellationToken cancellationToken = default);
}
