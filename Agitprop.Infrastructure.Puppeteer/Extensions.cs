using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        builder.Services.ConfigureInfrastructureWithoutBrowser(useProxies);
        builder.Services.AddBrowserPageLoader(useProxies);

        return builder;
    }

    /// <summary>
    /// Registers the browser page loader. Shared with callers that build their own service
    /// collection instead of an application builder.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="useProxies">Indicates whether to load pages through the proxy pool.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddBrowserPageLoader(this IServiceCollection services, bool useProxies = false)
    {
        if (useProxies)
        {
            services.AddTransient<IBrowserPageLoader, PuppeteerPageLoaderWithProxies>();
        }
        else
        {
            services.AddTransient<IBrowserPageLoader, PuppeteerPageLoader>();
        }

        return services;
    }
}
