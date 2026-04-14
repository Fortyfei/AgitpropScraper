using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agitprop.Core.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Agitprop.Sinks.Newsfeed;
using System;

namespace Agitprop.Scraper.Consumer.Consumers
{
    /// <summary>
    /// Consumes newsfeed job descriptions and processes them using a web scraping spider.
    /// </summary>
    public class NewsfeedJobConsumer : IConsumer<NewsfeedJobDescrpition>
    {
        private readonly ISpider _spider;
        private readonly ILogger<NewsfeedJobConsumer> _logger;
        private readonly NewsfeedSink _sink;
        private static readonly ActivitySource _activitySource = new("Agitprop.NewsfeedJobConsumer");
        private static readonly Meter _meter = new("Agitprop.NewsfeedJobConsumer");
        private static readonly Counter<long> _jobFailures = _meter.CreateCounter<long>(
            "newsfeed.job.failures",
            description: "Total failed newsfeed scraping jobs grouped by exception type, domain, URL, and exception message");

        public NewsfeedJobConsumer(
            ISpider spider,
            ILogger<NewsfeedJobConsumer> logger,
            NewsfeedSink sink)
        {
            _spider = spider;
            _logger = logger;
            _sink = sink;
        }

        private static void RecordJobFailure(string url, Exception ex)
        {
            _jobFailures.Add(1,
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                new KeyValuePair<string, object?>("exception_message", InternalExtensions.GetExceptionMessage(ex)),
                new KeyValuePair<string, object?>("domain", InternalExtensions.GetDomainFromUrl(url)),
                new KeyValuePair<string, object?>("url", url));
        }

        public async Task Consume(ConsumeContext<NewsfeedJobDescrpition> context)
        {
            using var activity = _activitySource.StartActivity("Consume", ActivityKind.Consumer);
            var descriptor = context.Message;
            activity?.SetTag("job.url", descriptor.Url);
            activity?.SetTag("job.type", descriptor.Type.ToString());

            _logger.LogInformation("Crawling started for URL: {Url}", descriptor.Url);

            try
            {
                var job = descriptor.ConvertToScrapingJob();

                List<Core.ScrapingJobDescription> newJobs = await _spider.CrawlAsync(job, _sink, context.CancellationToken);

                _logger.LogInformation("Crawling finished for URL: {Url}, new jobs found: {Count}", job.Url, newJobs.Count);

                if (newJobs.Count > 0)
                {
                    // Create a span for publishing the new jobs
                    using var publishActivity = _activitySource.StartActivity("PublishNewJobs", ActivityKind.Producer);
                    var idk = newJobs.Select(x => (NewsfeedJobDescrpition)x).ToList();
                    publishActivity?.SetTag("publish.jobs.count", idk.Count);
                    await context.PublishBatch(idk);
                    _logger.LogInformation("Published {Count} new jobs from URL: {Url}", idk.Count, job.Url);
                    publishActivity?.SetStatus(ActivityStatusCode.Ok);
                }

                activity?.SetStatus(ActivityStatusCode.Ok, "Job processed successfully");
            }
            catch (ArgumentException ex)
            {
                RecordJobFailure(descriptor.Url, ex);
                _logger.LogError(ex, "Invalid argument in newsfeed job for URL: {Url}", descriptor.Url);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Do not rethrow to avoid poison messages
            }
            catch (Exception ex)
            {
                RecordJobFailure(descriptor.Url, ex);
                _logger.LogError(ex, "Newsfeed job failed for URL: {Url}", descriptor.Url);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }
}
