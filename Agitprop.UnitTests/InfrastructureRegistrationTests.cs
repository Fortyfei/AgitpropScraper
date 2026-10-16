using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.Puppeteer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agitprop.UnitTests;

public class InfrastructureRegistrationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ConfigureInfrastructureWithBrowser_RegistersSharedAndBrowserServices(bool useProxies)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureInfrastructureWithBrowser(useProxies);

        using var host = builder.Build();

        Assert.That(host.Services.GetRequiredService<ISpider>(), Is.Not.Null);
        Assert.That(host.Services.GetRequiredService<IStaticPageLoader>(), Is.Not.Null);
        Assert.That(host.Services.GetRequiredService<IBrowserPageLoader>(), Is.Not.Null);
        Assert.That(host.Services.GetRequiredService<IPageTransport>(), Is.TypeOf<PageTransport>());
        Assert.That(host.Services.GetService<IProxyPool>() is not null, Is.EqualTo(useProxies));
    }
}
