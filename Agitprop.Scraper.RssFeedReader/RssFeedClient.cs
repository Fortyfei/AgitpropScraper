using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;

namespace Agitprop.Scraper.RssFeedReader;

public enum RssFeedOutcome
{
    Success,
    NotModified,
    Failed
}

/// <summary>
/// Result of one feed read. <see cref="Links"/> holds normalized article links (no query string or
/// fragment) in document order, already bounded by <see cref="RssFeedReaderOptions.MaxItemsPerFeed"/>.
/// </summary>
public sealed record RssFeedFetchResult
{
    public required RssFeedOutcome Outcome { get; init; }

    public IReadOnlyList<string> Links { get; init; } = [];

    public int ItemsParsed { get; init; }

    public int ItemsInvalid { get; init; }

    public bool Truncated { get; init; }

    public string? EntityTag { get; init; }

    public string? LastModified { get; init; }

    public int? StatusCode { get; init; }

    /// <summary>One of http, timeout, invalid, parse when <see cref="Outcome"/> is Failed.</summary>
    public string? FailureReason { get; init; }
}

public interface IRssFeedClient
{
    Task<RssFeedFetchResult> FetchAsync(
        string feedUrl,
        string? entityTag,
        string? lastModified,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads one RSS/Atom feed: conditional GET, bounded download, DTD-safe parsing and link
/// normalization. It owns no telemetry; the reader turns the result into spans, metrics and logs.
/// </summary>
public sealed class RssFeedClient : IRssFeedClient
{
    public const string HttpClientName = "rss";

    private const string FailureReasonHttp = "http";
    private const string FailureReasonTimeout = "timeout";
    private const string FailureReasonInvalid = "invalid";
    private const string FailureReasonParse = "parse";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RssFeedReaderOptions _options;

    static RssFeedClient()
    {
        // Some feeds still declare a legacy code page (cp1250 for several Hungarian sources).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public RssFeedClient(IHttpClientFactory httpClientFactory, RssFeedReaderOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<RssFeedFetchResult> FetchAsync(
        string feedUrl,
        string? entityTag,
        string? lastModified,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        HttpResponseMessage response;
        try
        {
            // Resolved per fetch so the factory can rotate the pooled handler (and with it DNS).
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(feedUrl, entityTag, lastModified);
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new RssFeedFetchResult { Outcome = RssFeedOutcome.Failed, FailureReason = FailureReasonTimeout };
        }
        catch (Exception)
        {
            return new RssFeedFetchResult { Outcome = RssFeedOutcome.Failed, FailureReason = FailureReasonHttp };
        }

        using (response)
        {
            return await ReadResponseAsync(response, feedUrl, timeout.Token, cancellationToken);
        }
    }

    private async Task<RssFeedFetchResult> ReadResponseAsync(
        HttpResponseMessage response,
        string feedUrl,
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new RssFeedFetchResult { Outcome = RssFeedOutcome.NotModified, StatusCode = statusCode };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new RssFeedFetchResult
            {
                Outcome = RssFeedOutcome.Failed,
                FailureReason = FailureReasonHttp,
                StatusCode = statusCode
            };
        }

        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > _options.MaxFeedBytes)
        {
            return FailedInvalid(statusCode);
        }

        var baseUri = response.RequestMessage?.RequestUri ?? new Uri(feedUrl);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeoutToken);
            using var bounded = new LengthLimitingStream(stream, _options.MaxFeedBytes);
            using var reader = XmlReader.Create(bounded, XmlSettings, feedUrl);
            var feed = SyndicationFeed.Load(reader);

            return Parse(feed, baseUri, statusCode, response);
        }
        catch (FeedTooLargeException)
        {
            return FailedInvalid(statusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new RssFeedFetchResult
            {
                Outcome = RssFeedOutcome.Failed,
                FailureReason = FailureReasonTimeout,
                StatusCode = statusCode
            };
        }
        catch (Exception)
        {
            return new RssFeedFetchResult
            {
                Outcome = RssFeedOutcome.Failed,
                FailureReason = FailureReasonParse,
                StatusCode = statusCode
            };
        }
    }

    private RssFeedFetchResult Parse(
        SyndicationFeed feed,
        Uri baseUri,
        int statusCode,
        HttpResponseMessage response)
    {
        var links = new List<string>();
        var itemsParsed = 0;
        var itemsInvalid = 0;
        var truncated = false;

        foreach (var item in feed.Items)
        {
            itemsParsed++;

            var link = item.Links.Select(syndicationLink => syndicationLink.Uri).FirstOrDefault(uri => uri is not null);
            if (!TryNormalizeLink(link, baseUri, out var normalized))
            {
                itemsInvalid++;
                continue;
            }

            if (links.Count >= _options.MaxItemsPerFeed)
            {
                truncated = true;
                continue;
            }

            links.Add(normalized);
        }

        return new RssFeedFetchResult
        {
            Outcome = RssFeedOutcome.Success,
            Links = links,
            ItemsParsed = itemsParsed,
            ItemsInvalid = itemsInvalid,
            Truncated = truncated,
            EntityTag = response.Headers.ETag?.ToString(),
            LastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture),
            StatusCode = statusCode
        };
    }

    private static HttpRequestMessage CreateRequest(string feedUrl, string? entityTag, string? lastModified)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);

        if (!string.IsNullOrWhiteSpace(entityTag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", entityTag);
        }

        if (!string.IsNullOrWhiteSpace(lastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
        }

        return request;
    }

    private static bool TryNormalizeLink(Uri? link, Uri baseUri, out string normalized)
    {
        normalized = string.Empty;

        if (link is null)
        {
            return false;
        }

        Uri absolute;
        try
        {
            absolute = link.IsAbsoluteUri ? link : new Uri(baseUri, link);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalized = absolute.GetLeftPart(UriPartial.Path);
        return !string.IsNullOrEmpty(normalized);
    }

    private static RssFeedFetchResult FailedInvalid(int statusCode) => new()
    {
        Outcome = RssFeedOutcome.Failed,
        FailureReason = FailureReasonInvalid,
        StatusCode = statusCode
    };

    private sealed class FeedTooLargeException(long limit)
        : Exception($"The feed exceeds the configured {limit} byte limit.");

    /// <summary>
    /// Counts the bytes read by the parser and aborts the download once the configured limit is
    /// exceeded, so a hostile or broken feed cannot balloon the heap before the parser finishes.
    /// </summary>
    private sealed class LengthLimitingStream(Stream inner, long limit) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override int ReadByte()
        {
            var value = inner.ReadByte();
            if (value >= 0)
            {
                Count(1);
            }

            return value;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int bytesRead)
        {
            _bytesRead += bytesRead;
            if (_bytesRead > limit)
            {
                throw new FeedTooLargeException(limit);
            }

            return bytesRead;
        }
    }
}
