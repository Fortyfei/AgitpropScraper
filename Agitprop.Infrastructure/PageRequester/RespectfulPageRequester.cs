using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Security;
using Agitprop.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure.PageRequester;

/// <summary>
/// A page requester that enforces domain-based respectful scraping rules.
/// </summary>
public class RespectfulPageRequester : IPageRequester
{
    private static readonly Meter _meter = new("Agitprop.RespectfulPageRequester");
    private static readonly ActivitySource _activitySource = new("Agitprop.PageRequester.RespectfulPageRequester");
    private static readonly Counter<long> _totalRequests = _meter.CreateCounter<long>("respectful.requests.total", description: "Total respectful direct requests");
    private static readonly Counter<long> _rateLimitWaits = _meter.CreateCounter<long>("respectful.rate_limit.waits", description: "Number of rate limit waits during respectful scraping");
    private static readonly Histogram<double> _rateLimitWaitDuration = _meter.CreateHistogram<double>("respectful.rate_limit.wait_duration", "ms", "Wait duration for respectful scraping rate limiting");
    private static readonly Histogram<double> _requestLatency = _meter.CreateHistogram<double>("respectful.request.latency", "ms", "HTTP request latency for respectful direct requests");

    private readonly HttpClient _httpClient;
    private readonly ILogger<RespectfulPageRequester>? _logger;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _domainRequestLog = new();
    private const int MaxRequestsPerMinute = 10;
    private static readonly TimeSpan RequestWindow = TimeSpan.FromMinutes(1);

    public RespectfulPageRequester(CookieContainer? cookieContainer = null, ILogger<RespectfulPageRequester>? logger = default)
    {
        CookieContainer = cookieContainer ?? new CookieContainer();
        _logger = logger;
        _httpClient = CreateClient();
    }

    public CookieContainer CookieContainer { get; set; }

    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        var uri = new Uri(url);
        await EnforceDomainRateLimitAsync(uri);

        using var activity = _activitySource.StartActivity("Request", ActivityKind.Internal);
        activity?.SetTag("url", url);
        activity?.SetTag("domain", uri.Host);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.GetAsync(url);
            return response;
        }
        finally
        {
            stopwatch.Stop();
            _totalRequests.Add(1, new KeyValuePair<string, object?>("domain", uri.Host));
            _requestLatency.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("domain", uri.Host));
        }
    }

    private async Task EnforceDomainRateLimitAsync(Uri uri)
    {
        var domain = uri.Host;
        var now = DateTime.UtcNow;
        var windowStart = now - RequestWindow;
        var queue = _domainRequestLog.GetOrAdd(domain, _ => new Queue<DateTime>());

        TimeSpan waitTime = TimeSpan.Zero;
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < windowStart)
            {
                queue.Dequeue();
            }

            if (queue.Count >= MaxRequestsPerMinute)
            {
                waitTime = (queue.Peek() + RequestWindow) - now;
            }
            else
            {
                queue.Enqueue(now);
            }
        }

        if (waitTime > TimeSpan.Zero)
        {
            _logger?.LogInformation("Respectful scraping wait: {WaitTime} for domain {Domain}", waitTime, domain);
            _rateLimitWaits.Add(1, new KeyValuePair<string, object?>("domain", domain));
            _rateLimitWaitDuration.Record(waitTime.TotalMilliseconds, new KeyValuePair<string, object?>("domain", domain));
            await Task.Delay(waitTime);
            lock (queue)
            {
                queue.Enqueue(DateTime.UtcNow);
            }
        }
    }

    private HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            CookieContainer = CookieContainer,
            UseCookies = true
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36");
        return client;
    }
}
