namespace Agitprop.Core.Interfaces;

/// <summary>
/// Defines the contract for loading static web pages.
/// </summary>
public interface IStaticPageLoader
{
    /// <summary>
    /// Loads the content of a static web page from the specified URL.
    /// </summary>
    /// <param name="url">The URL of the page to load.</param>
    /// <param name="cancellationToken">A token used to cancel the page load.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the content of the page as a string.</returns>
    Task<string> Load(string url, CancellationToken cancellationToken = default);
}
