using System.Net;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;

namespace Agitprop.UnitTests;

public class HttpStaticPageLoaderCancellationTests
{
    [Test]
    public async Task Load_WhenRequestIsCanceled_DoesNotRetry()
    {
        var requester = new BlockingPageRequester();
        var loader = new HttpStaticPageLoader(requester, new TestCookiesStorage());
        using var cancellation = new CancellationTokenSource();

        var loadTask = loader.Load("https://example.test/article", cancellation.Token);
        await requester.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await loadTask);
        Assert.That(requester.CallCount, Is.EqualTo(1));
    }

    private sealed class BlockingPageRequester : IPageRequester
    {
        public CookieContainer CookieContainer { get; set; } = new();
        public int CallCount { get; private set; }
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HttpResponseMessage> GetAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TestCookiesStorage : ICookiesStorage
    {
        public Task AddAsync(CookieContainer cookieCollection) => Task.CompletedTask;

        public Task<CookieContainer> GetAsync() => Task.FromResult(new CookieContainer());
    }
}
