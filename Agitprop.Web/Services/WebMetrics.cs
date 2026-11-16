using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agitprop.Web.Services;

internal static class WebMetrics
{
    private static readonly Meter Meter = new("Agitprop.Web");
    private static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>(
        "query.cache.requests",
        description: "Dashboard query cache hits and misses by operation");

    public static void RecordCacheResult(Activity? activity, string operation, string result)
    {
        activity?.SetTag("cache.result", result);

        CacheRequests.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", result));
    }
}
