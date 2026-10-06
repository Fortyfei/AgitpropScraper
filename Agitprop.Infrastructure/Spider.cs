using Polly;

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Exceptions;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agitprop.Infrastructure;

public sealed class Spider(
    IPageTransport pageTransport,
    IConfiguration configuration,
    ILogger<Spider>? logger = default) : ISpider
{
    private readonly ILogger<Spider>? _logger = logger;
    private readonly IPageTransport _pageTransport = pageTransport;
    private readonly IConfiguration _configuration = configuration;
    private static readonly ActivitySource _activitySource = new("Agitprop.Spider");

    private static readonly Meter _meter = new("Agitprop.Spider");
    private static readonly Counter<long> _pagesProcessed = _meter.CreateCounter<long>(
        "spider.pages.processed",
        description: "Total pages successfully processed");
    private static readonly Counter<long> _scrapeFailures = _meter.CreateCounter<long>(
        "spider.scrape.failures",
        description: "Total scrape failures grouped by operation and page type");
    private static readonly Histogram<double> _processingTime = _meter.CreateHistogram<double>(
        "spider.processing.duration",
        "ms",
        "Time spent processing a page");

    public async Task<List<ScrapingJobDescription>> CrawlAsync(ScrapingJob job, ISink sink, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("CrawlAsync", ActivityKind.Internal);
        activity?.SetTag("url", TelemetryUrl.RedactQueryAndFragment(job.Url));
        activity?.SetTag("page_type", job.PageType.ToString());
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";
        var pageProcessed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await sink.CheckPageAlreadyVisited(job.Url))
            {
                outcome = "skipped";
                _logger?.LogDebug("Page already visited: {Url}", TelemetryUrl.RedactQueryAndFragment(job.Url));
                activity?.SetStatus(ActivityStatusCode.Ok, "Already visited");
                return [];
            }

            HtmlDocument doc = await LoadPageAsync(job, cancellationToken);

            if (job.PageCategory == PageCategory.TargetPage)
            {
                await ProcessTargetPage(job, doc, sink, cancellationToken);
                pageProcessed = true;
                activity?.SetStatus(ActivityStatusCode.Ok);
                return [];
            }

            var result = await ProcessPage(job, doc, sink, cancellationToken);
            pageProcessed = true;
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
            throw;
        }
        catch (Exception ex)
        {
            outcome = "failure";
            _scrapeFailures.Add(1,
                new KeyValuePair<string, object?>("operation", "crawl"),
                new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _processingTime.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("page_type", job.PageType.ToString()),
                new KeyValuePair<string, object?>("outcome", outcome));
            if (pageProcessed)
            {
                _pagesProcessed.Add(1, new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));
            }
        }
    }

    private void RecordSubOperationFailure(string operation, ScrapingJob job, Exception exception)
    {
        _scrapeFailures.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));
        _logger?.LogError(exception, "Scrape sub-operation {Operation} failed for {Url}",
            operation, TelemetryUrl.RedactQueryAndFragment(job.Url));
    }

    private async Task<List<ScrapingJobDescription>> ProcessPage(ScrapingJob job, HtmlDocument doc, ISink sink, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("ProcessPage", ActivityKind.Internal);
        activity?.SetTag("url", TelemetryUrl.RedactQueryAndFragment(job.Url));

        // Process link extraction pages
        List<ScrapingJobDescription> newJobs = new();
        foreach (var parser in job.LinkParsers)
        {
            try
            {
                var links = await parser.GetLinksAsync(job.Url, doc.ParsedText);
                newJobs.AddRange(links);
            }
            catch (Exception ex)
            {
                RecordSubOperationFailure("link_parser", job, ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }

        // Handle pagination
        if (job.PageCategory == PageCategory.PageWithPagination && _configuration.GetValue<bool>("Continous"))
        {
            try
            {
                var nextPage = await job.Pagination!.GetNextPageAsync(job.Url, doc.DocumentNode.OuterHtml);
                newJobs.Add(nextPage);
            }
            catch (Exception ex)
            {
                RecordSubOperationFailure("pagination", job, ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
        }

        return newJobs;
    }

    private async Task ProcessTargetPage(ScrapingJob job, HtmlDocument doc, ISink sink, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ProcessTargetPage", ActivityKind.Internal);
        activity?.SetTag("url", TelemetryUrl.RedactQueryAndFragment(job.Url));

        var retryCount = _configuration.GetValue<int>("Retry:Spider", 3);
        List<ContentParserResult> results = new();

        foreach (var parser in job.ContentParsers)
        {
            try
            {
                var parsed = await parser.ParseContentAsync(doc);

                results.Add(parsed);
            }
            catch (Exception ex)
            {
                RecordSubOperationFailure("content_parser", job, ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw new ContentParserException($"Failed content parsing for {job.Url}", ex);
            }
        }

        if (!results.Any())
        {
            _logger?.LogError("No content scraped from: {Url}", TelemetryUrl.RedactQueryAndFragment(job.Url));
            _scrapeFailures.Add(1,
                new KeyValuePair<string, object?>("operation", "content_parser"),
                new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));
            activity?.SetStatus(ActivityStatusCode.Error, "No content scraped");
            throw new ContentParserException($"No content scraped from: {job.Url}");
        }

        _logger?.LogDebug("Sending scraped data to sink for {Url}", TelemetryUrl.RedactQueryAndFragment(job.Url));
        await sink.EmitAsync(job.Url, results, cancellationToken);
        _logger?.LogDebug("Finished processing target page: {Url}", TelemetryUrl.RedactQueryAndFragment(job.Url));
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private async Task<HtmlDocument> LoadPageAsync(ScrapingJob job, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("LoadPageAsync", ActivityKind.Internal);
        bool isHeadless = _configuration.GetValue<bool>("Headless");

        _logger?.LogDebug("Loading page with transport: {Url}", TelemetryUrl.RedactQueryAndFragment(job.Url));

        var result = await _pageTransport.LoadAsync(job.Url, new PageLoadOptions(
            RequiresJavaScript: job.PageType == PageType.Dynamic,
            Actions: job.Actions,
            Headless: isHeadless), cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(result.Content);
        return doc;
    }
}
