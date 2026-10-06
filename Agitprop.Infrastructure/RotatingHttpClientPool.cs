using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading;

using Agitprop.Core;
using Agitprop.Core.Interfaces;

using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure;

public class RotatingHttpClientPool
{
    private readonly IProxyPool _pool;
    private readonly ILogger<RotatingHttpClientPool>? _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.RotatingHttpClientPool");
    private readonly List<string> _defaultUserAgents;

    public RotatingHttpClientPool(IProxyPool pool, ILogger<RotatingHttpClientPool>? logger = null)
    {
        _pool = pool;
        _logger = logger;
        _defaultUserAgents =
        [
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/118.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36 Edg/117.0.2045.47",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Safari/605.1.15",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:109.0) Gecko/20100101 Firefox/118.0",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/118.0",
            "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Mobile Safari/537.36",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1"
        ];
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("SendAsync", ActivityKind.Internal);
        
        // Clone the request to avoid issues
        var requestClone = CloneRequest(request);
        
        try
        {
            activity?.SetTag("http.method", request.Method.Method);
            var safeUrl = TelemetryUrl.RedactQueryAndFragment(request.RequestUri?.ToString() ?? string.Empty);
            activity?.SetTag("http.url", safeUrl);

            // Randomize headers: user-agent, accept-language
            if (!requestClone.Headers.UserAgent.Any())
            {
                var ua = _defaultUserAgents[new Random().Next(_defaultUserAgents.Count)];
                requestClone.Headers.UserAgent.ParseAdd(ua);
                _logger?.LogTrace("Assigned User-Agent '{UA}' to request {Method} {Url}", ua,
                    requestClone.Method, TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty));
                activity?.SetTag("http.user_agent", ua);
            }

            if (!requestClone.Headers.AcceptLanguage.Any())
            {
                requestClone.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            }

            // Get proxy address (this will block and retry internally if needed)
            var proxyAddress = await _pool.GetNextProxyAsync(ct);
            if (string.IsNullOrEmpty(proxyAddress))
            {
                var exception = new InvalidOperationException("No proxy address available");
                _logger?.LogError(exception, "No proxy address available when sending {Method} {Url}",
                    requestClone.Method, TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty));
                activity?.SetStatus(ActivityStatusCode.Error, "No proxy address available");
                throw exception;
            }

            activity?.SetTag("proxy.address", proxyAddress);
            
            _logger?.LogDebug("Selected proxy {Proxy} for request {Method} {Url}", 
                proxyAddress, requestClone.Method,
                TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty));

            // Create HTTP client with proxy for this request
            using var invoker = CreateInvokerForProxy(proxyAddress);

            // Send via invoker
            try
            {
                var resp = await invoker.SendAsync(requestClone, ct);
                activity?.SetTag("http.status_code", (int)resp.StatusCode);
                activity?.SetStatus(ActivityStatusCode.Ok);
                _logger?.LogDebug("Request {Method} {Url} via proxy {Proxy} returned {StatusCode}", 
                    requestClone.Method, TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty),
                    proxyAddress, resp.StatusCode);
                
                // Mark proxy as successful
                await _pool.MarkSuccessAsync(proxyAddress);
                
                return resp;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Request {Method} {Url} via proxy {Proxy} failed", 
                    requestClone.Method, TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty),
                    proxyAddress);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                // Mark proxy as dead
                await _pool.MarkDeadAsync(proxyAddress);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Log and rethrow
            _logger?.LogError(ex, "Failed to send request {Method} {Url}", requestClone.Method,
                TelemetryUrl.RedactQueryAndFragment(requestClone.RequestUri?.ToString() ?? string.Empty));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
    
    private HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        
        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        // Copy content if present
        if (request.Content != null)
        {
            var content = request.Content.ReadAsStreamAsync().Result;
            clone.Content = new StreamContent(content);
            
            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        return clone;
    }

    private HttpMessageInvoker CreateInvokerForProxy(string addr)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy(addr),
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 100,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        
        _logger?.LogTrace("Created SocketsHttpHandler for proxy {Address} with connect timeout: {Timeout}s", addr, 10);

        var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        _logger?.LogTrace("Created HttpMessageInvoker for proxy {Address}", addr);
        return invoker;
    }
}
