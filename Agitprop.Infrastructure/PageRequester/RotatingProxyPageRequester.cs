using System.Net;

using Agitprop.Core;
using Agitprop.Core.Interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure;

    
public class RotatingProxyPageRequester : IPageRequester
{
    private readonly RotatingHttpClientPool _pool;
    private readonly IConfiguration _config;
    private readonly ILogger<RotatingProxyPageRequester>? _logger;

    public CookieContainer CookieContainer { get; set; } = new CookieContainer();

    public RotatingProxyPageRequester(RotatingHttpClientPool pool, IConfiguration config, ILogger<RotatingProxyPageRequester>? logger = null)
    {
        _pool = pool;
        _config = config;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        // CookieContainer is handled by SocketsHttpHandler in invoker; ensure handlers use it if needed.

        try
        {
            var resp = await _pool.SendAsync(req);
            return resp;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Request via rotating proxy failed for {Url}",
                TelemetryUrl.RedactQueryAndFragment(url));
            throw;
        }
    }
}
