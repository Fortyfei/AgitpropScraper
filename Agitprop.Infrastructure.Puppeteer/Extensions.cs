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

        if (useProxies)
        {
            builder.Services.AddTransient<IBrowserPageLoader, PuppeteerPageLoaderWithProxies>();
        }
        else
        {
            builder.Services.AddTransient<IBrowserPageLoader, PuppeteerPageLoader>();
        }

        return builder;
    }
}
