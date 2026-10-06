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
        private static readonly Counter<long> _jobsConsumed = _meter.CreateCounter<long>(
            "newsfeed.jobs.consumed",
            description: "Total newsfeed jobs consumed");
        private static readonly Counter<long> _jobsCompleted = _meter.CreateCounter<long>(
            "newsfeed.jobs.completed",
            description: "Total newsfeed jobs completed successfully");
        private static readonly Counter<long> _jobFailures = _meter.CreateCounter<long>(
            "newsfeed.job.failures",
            description: "Total failed newsfeed scraping jobs grouped by job type and handling outcome");
        private static readonly Counter<long> _publishFailures = _meter.CreateCounter<long>(
            "newsfeed.publish.failures",
            description: "Total failures publishing discovered newsfeed jobs");
        private static readonly Histogram<double> _jobDuration = _meter.CreateHistogram<double>(
            "newsfeed.job.duration",
            "ms",
            "Time spent processing a newsfeed job");

        public NewsfeedJobConsumer(
            ISpider spider,
            ILogger<NewsfeedJobConsumer> logger,
            NewsfeedSink sink)
        {
            _spider = spider;
            _logger = logger;
            _sink = sink;
        }

        private static void RecordJobFailure(string jobType, string handling)
        {
            _jobFailures.Add(1,
                new KeyValuePair<string, object?>("job_type", jobType),
                new KeyValuePair<string, object?>("handling", handling));
        }

        public async Task Consume(ConsumeContext<NewsfeedJobDescrpition> context)
        {
            using var activity = _activitySource.StartActivity("Consume", ActivityKind.Consumer);
            var descriptor = context.Message;
            var safeUrl = Core.TelemetryUrl.RedactQueryAndFragment(descriptor.Url);
            var jobType = descriptor.Type.ToString();
            var stopwatch = Stopwatch.StartNew();
            var outcome = "success";
            activity?.SetTag("job.url", safeUrl);
            activity?.SetTag("job.type", descriptor.Type.ToString());
            _jobsConsumed.Add(1, new KeyValuePair<string, object?>("job_type", jobType));

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["MessageId"] = context.MessageId,
                ["CorrelationId"] = context.CorrelationId,
                ["JobType"] = jobType
            });

            try
            {
                var job = descriptor.ConvertToScrapingJob();

                List<Core.ScrapingJobDescription> newJobs = await _spider.CrawlAsync(job, _sink, context.CancellationToken);

                _logger.LogInformation("Newsfeed job completed for {Url}; discovered {Count} jobs",
                    Core.TelemetryUrl.RedactQueryAndFragment(job.Url), newJobs.Count);

                if (newJobs.Count > 0)
                {
                    using var publishActivity = _activitySource.StartActivity("PublishNewJobs", ActivityKind.Producer);
                    var idk = newJobs.Select(x => (NewsfeedJobDescrpition)x).ToList();
                    publishActivity?.SetTag("publish.jobs.count", idk.Count);
                    try
                    {
                        await context.PublishBatch(idk);
                        _logger.LogDebug("Published {Count} new jobs from {Url}",
                            idk.Count, Core.TelemetryUrl.RedactQueryAndFragment(job.Url));
                        publishActivity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    catch (Exception ex)
                    {
                        _publishFailures.Add(1, new KeyValuePair<string, object?>("job_type", jobType));
                        publishActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        throw;
                    }
                }

                _jobsCompleted.Add(1, new KeyValuePair<string, object?>("job_type", jobType));
                activity?.SetStatus(ActivityStatusCode.Ok, "Job processed successfully");
            }
            catch (ArgumentException ex)
            {
                outcome = "handled_failure";
                RecordJobFailure(jobType, "handled");
                _logger.LogError(ex, "Invalid argument in newsfeed job for {Url}", safeUrl);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            catch (Exception ex)
            {
                outcome = "failure";
                RecordJobFailure(jobType, "propagated");
                _logger.LogError(ex, "Newsfeed job failed for {Url}", safeUrl);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                _jobDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("job_type", jobType),
                    new KeyValuePair<string, object?>("outcome", outcome));
            }
        }
    }
}
