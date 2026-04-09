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
    private readonly ActivitySource _activitySource = new("Agitprop.Spider");

    // Performance Metrics
    private readonly Counter<long> _pagesProcessed = new Meter("Agitprop.Spider").CreateCounter<long>("spider.pages.processed", description: "Total pages processed");
    private readonly Histogram<double> _processingTime = new Meter("Agitprop.Spider").CreateHistogram<double>("spider.processing.time", "ms", "Total processing time per page");

    public async Task<List<ScrapingJobDescription>> CrawlAsync(ScrapingJob job, ISink sink, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("CrawlAsync", ActivityKind.Internal);
        activity?.SetTag("url", job.Url);
        activity?.SetTag("page_type", job.PageType.ToString());
        cancellationToken.ThrowIfCancellationRequested();

        // Check if already visited
        if (await sink.CheckPageAlreadyVisited(job.Url))
        {
            _logger?.LogInformation("Page already visited: {Url}", job.Url);
            activity?.SetStatus(ActivityStatusCode.Ok, "Already visited");
            return [];
        }

        var processingStartTime = Stopwatch.StartNew();

        HtmlDocument doc = await LoadPageAsync(job, cancellationToken);

        // Process the page
        if (job.PageCategory == PageCategory.TargetPage)
        {
            await ProcessTargetPage(job, doc, sink, cancellationToken);
            return [];
        }

        var result = await ProcessPage(job, doc, sink, cancellationToken);

        // Record successful processing
        var processingTime = processingStartTime.Elapsed.TotalMilliseconds;
        _processingTime.Record(processingTime, new KeyValuePair<string, object?>("url", job.Url), new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));
        _pagesProcessed.Add(1, new KeyValuePair<string, object?>("url", job.Url), new KeyValuePair<string, object?>("page_type", job.PageType.ToString()));

        _logger?.LogInformation("Page processed in {ProcessingTime}ms: {Url}", processingTime, job.Url);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return result;
    }

    private async Task<List<ScrapingJobDescription>> ProcessPage(ScrapingJob job, HtmlDocument doc, ISink sink, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("ProcessPage", ActivityKind.Internal);

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
                _logger?.LogError(ex, "Failed to get links from site: {Url}", job.Url);
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
                _logger?.LogError(ex, "Failed to get next page for site: {Url}", job.Url);
            }
        }

        return newJobs;
    }

    private async Task ProcessTargetPage(ScrapingJob job, HtmlDocument doc, ISink sink, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ProcessTargetPage", ActivityKind.Internal);
        activity?.SetTag("url", job.Url);

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
                _logger?.LogError(ex, "Failed to run content parser on {Url}", job.Url);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw new ContentParserException($"Failed content parsing for {job.Url}", ex);
            }
        }

        if (!results.Any())
        {
            _logger?.LogError("No content scraped from: {Url}", job.Url);
            activity?.SetStatus(ActivityStatusCode.Error, "No content scraped");
            throw new ContentParserException($"No content scraped from: {job.Url}");
        }

        _logger?.LogInformation("Sending scraped data to sink: {Url}", job.Url);
        await sink.EmitAsync(job.Url, results, cancellationToken);
        _logger?.LogInformation("Finished processing target page: {Url}", job.Url);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private async Task<HtmlDocument> LoadPageAsync(ScrapingJob job, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("LoadPageAsync", ActivityKind.Internal);
        bool isHeadless = _configuration.GetValue<bool>("Headless");

        _logger?.LogInformation("Loading page with transport: {Url}", job.Url);

        var result = await _pageTransport.LoadAsync(job.Url, new PageLoadOptions(
            RequiresJavaScript: job.PageType == PageType.Dynamic,
            Actions: job.Actions,
            Headless: isHeadless), cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(result.Content);
        return doc;
    }
}
