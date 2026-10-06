using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agitprop.Web.Api;

internal static class ApiMetrics
{
    private static readonly Meter Meter = new("Agitprop.Web.Api");
    private static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>(
        "api.cache.requests",
        description: "API response cache hits and misses by operation");

    public static void RecordCacheResult(Activity? activity, string operation, string result)
    {
        activity?.SetTag("cache.result", result);
        if (result == "hit")
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        CacheRequests.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", result));
    }
}
