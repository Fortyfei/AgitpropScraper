using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.PageRequester;
using Agitprop.Infrastructure.ProxyProviders;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure.Puppeteer;

/// <summary>
/// Provides extension methods for configuring Puppeteer-based infrastructure services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Configures infrastructure services with Puppeteer browser support, with optional proxy support.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="useProxies">Indicates whether to use proxies for HTTP requests.</param>
    /// <returns>The updated host application builder.</returns>
    public static IHostApplicationBuilder ConfigureInfrastructureWithBrowser(this IHostApplicationBuilder builder, bool useProxies = false)
    {

        builder.Services.AddTransient<ISpider, Spider>();

        builder.Services.AddTransient<ICookiesStorage, CookieStorage>();
        builder.Services.AddTransient<IStaticPageLoader, HttpStaticPageLoader>();

        if (useProxies)
        {
            builder.Services.AddHttpClient<ProxyScrapeProxyProvider>();
            builder.Services.AddSingleton<IProxyProvider, ProxyScrapeProxyProvider>();

            builder.Services.AddHttpClient<RedScrapeProxyProvider>();
            builder.Services.AddSingleton<IProxyProvider, RedScrapeProxyProvider>();

            builder.Services.AddSingleton<IProxyPool, ProxyPool>();
            builder.Services.AddSingleton<RotatingHttpClientPool>();
            builder.Services.AddTransient<IPageRequester, RotatingProxyPageRequester>();

            builder.Services.AddTransient<IBrowserPageLoader, PuppeteerPageLoaderWithProxies>();
        }
        else
        {
            builder.Services.AddTransient<IPageRequester, RespectfulPageRequester>();
            builder.Services.AddTransient<IBrowserPageLoader, PuppeteerPageLoader>();
        }

        builder.Services.AddSingleton<IPageTransport>(sp =>
            new PageTransport(
                sp.GetRequiredService<IStaticPageLoader>(),
                sp.GetRequiredService<IBrowserPageLoader>(),
                sp.GetRequiredService<ILogger<PageTransport>>()));

        return builder;
    }
}
