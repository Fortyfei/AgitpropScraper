using System.Net;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Microsoft.Extensions.Configuration;

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

    [Test]
    public void Load_OnHttpFailure_DoesNotExposeHeadersOrRawUrl()
    {
        var requester = new FixedResponsePageRequester(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Headers =
            {
                Location = new Uri("https://redirect.test/path?token=secret")
            }
        });
        requester.Response.Headers.TryAddWithoutValidation("Set-Cookie", "session=secret-cookie");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:PageLoader"] = "0"
            })
            .Build();
        var loader = new HttpStaticPageLoader(
            requester,
            new TestCookiesStorage(),
            configuration: configuration);
        const string url = "https://user:password@example.test/article?token=secret#fragment";

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.Load(url));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("https://example.test/article"));
            Assert.That(exception.Message, Does.Not.Contain("password"));
            Assert.That(exception.Message, Does.Not.Contain("token=secret"));
            Assert.That(exception.Message, Does.Not.Contain("secret-cookie"));
            Assert.That(exception.Data, Is.Empty);
        });
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

    private sealed class FixedResponsePageRequester(HttpResponseMessage response) : IPageRequester
    {
        public CookieContainer CookieContainer { get; set; } = new();
        public HttpResponseMessage Response { get; } = response;

        public Task<HttpResponseMessage> GetAsync(
            string url,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Response);
    }
}
