using System.Collections.Concurrent;
using System.Diagnostics;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.ProxyProviders;

using Microsoft.Extensions.Logging;

public sealed class ProxyPool : IProxyPool
{
    private readonly IEnumerable<IProxyProvider> _providers;
    private readonly ILogger<ProxyPool>? _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.ProxyPool");

    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly ConcurrentQueue<string> _working = new();     // round-robin
    private Queue<string> _providerQueue = new();                  // not yet validated

    private bool _disposed;

    public ProxyPool(
        IEnumerable<IProxyProvider> providers,
        ILogger<ProxyPool>? logger = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger;
    }

    public async Task<string> GetNextProxyAsync(CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("GetNextProxy", ActivityKind.Internal);

        await _lock.WaitAsync(ct);
        try
        {
            // 1) try working cache (round-robin)
            if (_working.TryDequeue(out var live))
            {
                _working.Enqueue(live);

                _logger?.LogDebug("Returning proxy from working set: {Proxy}", live);
                activity?.SetTag("proxy.source", "working");

                return live;
            }

            // 2) fallback to providerQueue
            if (_providerQueue.Count == 0)
            {
                _logger?.LogInformation("Provider queue empty. Fetching new proxy list...");
                activity?.AddEvent(new ActivityEvent("RefillProviderList"));

                await RefillFromProvidersAsync(ct);

                if (_providerQueue.Count == 0)
                {
                    _logger?.LogWarning("Provider list is empty after refill.");
                    activity?.SetStatus(ActivityStatusCode.Error, "No provider proxies available");
                    return string.Empty;
                }
            }

            var next = _providerQueue.Dequeue();

            _logger?.LogDebug("Returning proxy from provider queue: {Proxy}", next);
            activity?.SetTag("proxy.source", "providerQueue");

            return next;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkDeadAsync(string proxyAddress)
    {
        if (string.IsNullOrWhiteSpace(proxyAddress))
            return;

        using var activity = _activitySource.StartActivity("MarkDead", ActivityKind.Internal);
        activity?.SetTag("proxy.outcome", "dead");

        _logger?.LogWarning("MarkDead called for proxy {Proxy}", proxyAddress);

        await _lock.WaitAsync();
        try
        {
            RemoveFromWorkingSet(proxyAddress);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkSuccessAsync(string proxyAddress)
    {
        if (string.IsNullOrWhiteSpace(proxyAddress))
            return;

        using var activity = _activitySource.StartActivity("MarkSuccess", ActivityKind.Internal);
        activity?.SetTag("proxy.outcome", "success");

        _logger?.LogDebug("MarkSuccess called for proxy {Proxy}", proxyAddress);

        await _lock.WaitAsync();
        try
        {
            if (!_working.Contains(proxyAddress))
            {
                _working.Enqueue(proxyAddress);
                _logger?.LogInformation("Proxy moved to working set: {Proxy}", proxyAddress);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RefillFromProvidersAsync(CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("RefillFromProviders", ActivityKind.Internal);

        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            try
            {
                var list = await provider.FetchProxyAddressesAsync();
                foreach (var p in list.Shuffle())
                    all.Add(p);

                _logger?.LogInformation("Fetched {Count} proxies from provider {Provider}",
                    list.Count(), provider.GetType().Name);

                activity?.AddEvent(new ActivityEvent(
                    "ProviderProxiesFetched",
                    tags: new ActivityTagsCollection
                    {
                        { "proxy.provider", provider.GetType().Name },
                        { "proxy.count", list.Count() }
                    }));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Provider {Provider} failed to fetch proxies", provider.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            }
        }

        _providerQueue = new Queue<string>(all);

        activity?.SetTag("proxy.total_from_providers", all.Count);
        _logger?.LogInformation("Provider queue refilled with {Count} proxies", all.Count);
    }

    private void RemoveFromWorkingSet(string proxy)
    {
        var items = _working.ToList();
        var removed = items.RemoveAll(p => p.Equals(proxy, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            _working.Clear();
            foreach (var item in items)
                _working.Enqueue(item);

            _logger?.LogInformation("Proxy removed from working set: {Proxy}", proxy);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger?.LogDebug("Disposing ProxyPool...");

        _lock.Dispose();
        await Task.CompletedTask;
    }
}
