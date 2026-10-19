using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure.PageLoader;

namespace Agitprop.UnitTests;

public class PageTransportCancellationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LoadAsync_ForwardsCancellationTokenToSelectedLoader(bool requiresJavaScript)
    {
        var staticLoader = new RecordingStaticPageLoader();
        var browserLoader = new RecordingBrowserPageLoader();
        var transport = new PageTransport(staticLoader, browserLoader);
        using var cancellation = new CancellationTokenSource();

        await transport.LoadAsync(
            "https://example.test/article",
            new PageLoadOptions(RequiresJavaScript: requiresJavaScript),
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(
                requiresJavaScript ? browserLoader.CancellationToken : staticLoader.CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(staticLoader.CallCount, Is.EqualTo(requiresJavaScript ? 0 : 1));
            Assert.That(browserLoader.CallCount, Is.EqualTo(requiresJavaScript ? 1 : 0));
        });
    }

    private sealed class RecordingStaticPageLoader : IStaticPageLoader
    {
        public int CallCount { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<string> Load(string url, CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Task.FromResult("<html></html>");
        }
    }

    private sealed class RecordingBrowserPageLoader : IBrowserPageLoader
    {
        public int CallCount { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<string> Load(
            string url,
            object? pageActions,
            bool headless,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Task.FromResult("<html></html>");
        }
    }
}
