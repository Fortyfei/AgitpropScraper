using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure.ProxyProviders;

public class RedScrapeProxyProvider : IProxyProvider
{
    //TODO: ennek valaohgy dinamusan kéne lekérni a proxy listár, mert a seed változik
    private readonly ILogger<RedScrapeProxyProvider> _logger;
    private readonly ActivitySource _activitySource = new("Agitprop.ProxyProviders.RedScrapeProxyProvider");
    private const string _sourceUri = "https://free.redscrape.com/api/proxies?protocol=http&format=json";
    private readonly HttpClient _http;

    public RedScrapeProxyProvider(HttpClient http, ILogger<RedScrapeProxyProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IEnumerable<string>> FetchProxyAddressesAsync()
    {
        using var activity = _activitySource.StartActivity("FetchProxyAddressesAsync", ActivityKind.Internal);
        activity?.SetTag("proxy.source_uri", _sourceUri.ToString());
        try
        {
            var res = await _http.GetAsync(_sourceUri, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            var proxies = JsonSerializer.Deserialize<List<ProxyApiResponseItem>>(json);
            activity?.SetTag("proxy.fetched_count", proxies?.Count);
            _logger.LogInformation("Fetched {Count} proxy addresses from {Url}", proxies?.Count, _sourceUri);
            var addresses = proxies?.Select(p => $"{p.Address}:{p.Port}") ?? [];
            return addresses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch proxy addresses from {Url}", _sourceUri);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
    
    public class ProxyApiResponseItem
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = default!;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = default!;

    [JsonPropertyName("country")]
    public string Country { get; set; } = default!;

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = default!;

    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; set; }

    [JsonPropertyName("is_working")]
    public bool IsWorking { get; set; }

    [JsonPropertyName("last_checked")]
    public DateTime LastChecked { get; set; }
}
}
