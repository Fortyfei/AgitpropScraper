using System.Diagnostics;

using Agitprop.Core;

using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure.ProxyProviders;

public class ProxyScrapeProxyProvider : IProxyProvider
{
    private readonly ILogger<ProxyScrapeProxyProvider> _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.ProxyProviders.ProxyScrapeProxyProvider");
    private const string _sourceUri = "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&country=hr,ee,fi,si,cy,mt,pt,ad,lv,ie,dk,lt,at,gr,no,al,md,be,ge,sk,ro,me,cz,bg,it,se,by,ch,hu,gb,rs,tr,pl,es,ua,fr,de,nl&proxy_format=protocolipport&format=text&anonymity=Anonymous,Elite&timeout=20000";
    private readonly HttpClient _http;

    public ProxyScrapeProxyProvider(HttpClient http, ILogger<ProxyScrapeProxyProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IEnumerable<string>> FetchProxyAddressesAsync()
    {
        using var activity = _activitySource.StartActivity("FetchProxyAddressesAsync", ActivityKind.Internal);
        activity?.SetTag("proxy.source_uri", TelemetryUrl.RedactQueryAndFragment(_sourceUri));
        try
        {
            var res = await _http.GetAsync(_sourceUri, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            var addresses = json.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(addr => addr.Replace("http://", ""))
                                .ToArray();
            activity?.SetTag("proxy.fetched_count", addresses.Length);
            _logger.LogInformation("Fetched {Count} proxy addresses from {Url}", addresses.Length,
                TelemetryUrl.RedactQueryAndFragment(_sourceUri));
            return addresses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch proxy addresses from {Url}",
                TelemetryUrl.RedactQueryAndFragment(_sourceUri));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
