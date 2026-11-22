using System.Net;
using System.Text;

using Agitprop.Scraper.RssFeedReader;

namespace Agitprop.UnitTests;

public class RssFeedClientTests
{
    private const string FeedUrl = "https://site.hu/feed.xml";

    [Test]
    public async Task FetchAsync_NormalizesLinksAndCountsInvalidItems()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0">
              <channel>
                <title>Site</title>
                <link>https://site.hu/</link>
                <description>Feed</description>
                <item><title>First</title><link>https://site.hu/cikk/1?utm_source=rss#top</link></item>
                <item><title>Second</title><link>/cikk/2</link></item>
                <item><title>No link</title></item>
              </channel>
            </rss>
            """;

        var result = await CreateClient(new StubHandler(_ => Ok(xml)))
            .FetchAsync(FeedUrl, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.Success));
            Assert.That(result.Links, Is.EqualTo(new[] { "https://site.hu/cikk/1", "https://site.hu/cikk/2" }));
            Assert.That(result.ItemsParsed, Is.EqualTo(3));
            Assert.That(result.ItemsInvalid, Is.EqualTo(1));
            Assert.That(result.Truncated, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task FetchAsync_SendsValidatorsAndReportsNotModified()
    {
        string? sentEntityTag = null;
        string? sentLastModified = null;
        var handler = new StubHandler(request =>
        {
            sentEntityTag = request.Headers.TryGetValues("If-None-Match", out var values)
                ? string.Join(",", values)
                : null;
            sentLastModified = request.Headers.TryGetValues("If-Modified-Since", out var lastModified)
                ? string.Join(",", lastModified)
                : null;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var result = await CreateClient(handler)
            .FetchAsync(FeedUrl, "\"v1\"", "Wed, 21 Oct 2015 07:28:00 GMT", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.NotModified));
            Assert.That(result.Links, Is.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(304));
            Assert.That(sentEntityTag, Is.EqualTo("\"v1\""));
            Assert.That(sentLastModified, Is.EqualTo("Wed, 21 Oct 2015 07:28:00 GMT"));
        });
    }

    [Test]
    public async Task FetchAsync_NonSuccessStatus_ReportsHttpFailure()
    {
        var result = await CreateClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
            .FetchAsync(FeedUrl, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.Failed));
            Assert.That(result.FailureReason, Is.EqualTo("http"));
            Assert.That(result.StatusCode, Is.EqualTo(503));
        });
    }

    [Test]
    public async Task FetchAsync_MalformedXml_ReportsParseFailure()
    {
        var result = await CreateClient(new StubHandler(_ => Ok("<rss><channel><item>")))
            .FetchAsync(FeedUrl, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.Failed));
            Assert.That(result.FailureReason, Is.EqualTo("parse"));
        });
    }

    [Test]
    public async Task FetchAsync_FeedExceedingByteLimit_ReportsInvalidFailure()
    {
        var payload = Encoding.UTF8.GetBytes(FeedWithItems(200));
        var content = new UnboundedContent(payload);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var result = await CreateClient(handler, maxFeedBytes: 512)
            .FetchAsync(FeedUrl, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.Failed));
            Assert.That(result.FailureReason, Is.EqualTo("invalid"));
        });
    }

    [Test]
    public async Task FetchAsync_MoreItemsThanConfigured_Truncates()
    {
        var result = await CreateClient(new StubHandler(_ => Ok(FeedWithItems(3))), maxItemsPerFeed: 2)
            .FetchAsync(FeedUrl, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(RssFeedOutcome.Success));
            Assert.That(result.Links, Has.Count.EqualTo(2));
            Assert.That(result.ItemsParsed, Is.EqualTo(3));
            Assert.That(result.Truncated, Is.True);
        });
    }

    private static string FeedWithItems(int itemCount)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="utf-8"?><rss version="2.0"><channel><title>Site</title><link>https://site.hu/</link><description>Feed</description>""");
        for (var index = 1; index <= itemCount; index++)
        {
            builder.Append($"<item><title>Item {index}</title><link>https://site.hu/cikk/{index}</link></item>");
        }

        builder.Append("</channel></rss>");
        return builder.ToString();
    }

    private static RssFeedClient CreateClient(
        HttpMessageHandler handler,
        int maxItemsPerFeed = RssFeedReaderOptions.DefaultMaxItemsPerFeed,
        long maxFeedBytes = RssFeedReaderOptions.DefaultMaxFeedBytes) =>
        new(new StubHttpClientFactory(new HttpClient(handler)), new RssFeedReaderOptions
        {
            Feeds = [FeedUrl],
            Interval = TimeSpan.FromHours(1),
            RequestTimeout = TimeSpan.FromSeconds(10),
            MaxConcurrentFeeds = 1,
            MaxFeedBytes = maxFeedBytes,
            MaxItemsPerFeed = maxItemsPerFeed
        });

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static HttpResponseMessage Ok(string xml) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    /// <summary>
    /// Content without a Content-Length, so the client has to enforce the byte limit while reading.
    /// </summary>
    private sealed class UnboundedContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
