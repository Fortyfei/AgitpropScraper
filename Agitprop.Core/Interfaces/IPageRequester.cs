using System.Net;

namespace Agitprop.Core.Interfaces;

/// <summary>
/// Defines the contract for sending HTTP requests to web pages.
/// </summary>
public interface IPageRequester
{
    /// <summary>
    /// Gets or sets the cookie container for managing cookies.
    /// </summary>
    public CookieContainer CookieContainer { get; set; }

    /// <summary>
    /// Sends an HTTP GET request to the specified URL.
    /// </summary>
    /// <param name="url">The URL to send the GET request to.</param>
    /// <param name="cancellationToken">A token used to cancel the HTTP request.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the HTTP response message.</returns>
    Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken = default);
}
