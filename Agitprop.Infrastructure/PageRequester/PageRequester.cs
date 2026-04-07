using System.Net;
using System.Net.Security;

using Agitprop.Core.Interfaces;

namespace Agitprop.Infrastructure.PageRequester;

/// <summary>
/// A page requester that uses a static HTTP client for sending requests.
/// </summary>
public class PageRequester : IPageRequester
{
    // Static HTTP client instance for sending requests.
    private static HttpClient? client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageRequester"/> class with a specified cookie container.
    /// </summary>
    /// <param name="cookieContainer">The cookie container to use for managing cookies.</param>
public PageRequester(CookieContainer? cookieContainer = null)
{
    CookieContainer = cookieContainer ?? new CookieContainer();
        client = CreateClient();
    }

    /// <summary>
    /// Gets or sets the cookie container for managing cookies.
    /// </summary>
    public CookieContainer CookieContainer { get; set; }

    /// <summary>
    /// Sends an HTTP GET request to the specified URL.
    /// </summary>
    /// <param name="url">The URL to send the GET request to.</param>
    /// <returns>The HTTP response message.</returns>
    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await client!.GetAsync(url);
    }

    /// <summary>
    /// Creates an HTTP client with custom headers and a handler.
    /// </summary>
    /// <returns>An <see cref="HttpClient"/> instance.</returns>
    private HttpClient CreateClient()
    {
        var handler = GetHttpHandler();
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36");
        return client;
    }

    /// <summary>
    /// Creates an HTTP handler configured with custom settings.
    /// </summary>
    /// <returns>A <see cref="SocketsHttpHandler"/> instance.</returns>
    private SocketsHttpHandler GetHttpHandler()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10000,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Leave certs unvalidated for debugging
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            CookieContainer = CookieContainer,
            UseCookies = true
        };

        return handler;
    }
}
