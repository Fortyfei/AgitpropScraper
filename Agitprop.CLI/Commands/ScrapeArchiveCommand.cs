using System.CommandLine;
using Agitprop.CLI.Services;

namespace Agitprop.CLI.Commands;

public static class ScrapeArchiveCommand
{
    private static readonly string CommandName = "scrape-archive";
    private static readonly string DefaultFeedConfigPath = Path.Combine("Agitprop.Scraper.RssFeedReader", "appsettings.json");
    private static readonly IScrapeCommandOrchestrator _orchestrator = new ScrapeCommandOrchestrator();
    private static readonly IArchiveCommandInputResolver _inputResolver = new ArchiveCommandInputResolver();


    internal static Command AddScrapeArchiveCommand(this RootCommand rootCommand)
    {
        var dateOption = new Option<string>(
            ["--date", "-d"],
            () => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            "Specifies the date for scraping archives (format: yyyy-mm-dd, default: today). Cannot be combined with --from/--to.");

        var fromOption = new Option<string?>(
            ["--from"],
            "Start date for range scraping (format: yyyy-mm-dd). Must be used together with --to.");

        var toOption = new Option<string?>(
            ["--to"],
            "End date for range scraping (format: yyyy-mm-dd, inclusive). Must be used together with --from.");

        var newsiteOption = new Option<string[]>(
            ["--newsite", "-s"],
            "Specifies news sites to scrape (comma-separated or repeated). Supported keywords: all, default. If omitted, default is used.");

        var feedConfigOption = new Option<string>(
            ["--feed-config"],
            () => DefaultFeedConfigPath,
            "Path to appsettings JSON used to resolve default sites from the Feeds section.");

        var connectionOption = new Option<string>(
            ["--connection", "-c"],
            "RabbitMQ connection string for sending scraping jobs to queue (publishing is disabled when omitted)");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            () => false,
            "Backward-compatible alias for --verbosity detailed");

        var verbosityOption = new Option<string>(
            ["--verbosity"],
            () => "normal",
            "Output verbosity: quiet, normal, detailed");

        var scrapeArchiveCommand = new Command(CommandName, "Scrapes archives and lists article URLs")
        {
            dateOption,
            fromOption,
            toOption,
            newsiteOption,
            feedConfigOption,
            connectionOption,
            verboseOption,
            verbosityOption
        };

        scrapeArchiveCommand.SetHandler(async (string date, string? from, string? to, string[] newsites, string feedConfigPath, string connection, bool verbose, string verbosity) =>
                {
                    try
                    {
                        var inputResolution = _inputResolver.Resolve(new ArchiveCommandRawInput(
                            date,
                            from,
                            to,
                            newsites ?? [],
                            feedConfigPath,
                            verbose,
                            verbosity));

                        if (!inputResolution.Success)
                        {
                            Console.WriteLine($"Error: {inputResolution.ErrorMessage}");
                            Environment.ExitCode = 1;
                            return;
                        }

                        var input = inputResolution.ResolvedInput!;
                        var resolvedVerbosity = input.Verbosity;

                        foreach (var warning in input.Warnings)
                        {
                            Console.WriteLine(warning);
                        }

                        var dates = input.Dates;
                        var sites = input.Sites;
                        var failedRuns = new List<string>();
                        var successfulRuns = new List<string>();
                        int publishedCount = 0;
                        bool publishFailure = false;
                        bool publishingEnabled = false;

                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, "=== RUN CONFIG ===");
                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"Date mode: {(input.IsRangeMode ? "range" : "single")}");
                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"Dates: {string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd")))}");
                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"News sites ({sites.Count}): {string.Join(", ", sites.Select(s => s.ToString()))}");
                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"Publishing enabled: {(string.IsNullOrWhiteSpace(connection) ? "no" : "yes")}");
                        LogMessage(CliOutputVerbosity.Detailed, resolvedVerbosity, $"Feed config path: {input.ResolvedFeedConfigPath}");
                        LogMessage(CliOutputVerbosity.Detailed, resolvedVerbosity, $"Resolved verbosity: {resolvedVerbosity}");

                        foreach (var scrapeDate in dates)
                        {
                            LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, string.Empty);
                            LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"=== Date {scrapeDate:yyyy-MM-dd} ===");

                            foreach (var site in sites)
                            {
                                try
                                {
                                    LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"--- Scraping {site} ---");
                                    var siteResult = await _orchestrator.ExecuteArchiveSiteAsync(new ArchiveSiteScrapeRequest(site, scrapeDate));
                                    var jobResults = siteResult.Jobs;

                                    LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"Crawling finished for {siteResult.SourceUrl}. Articles found: {jobResults.Count}");
                                    if (jobResults.Any())
                                    {
                                        LogMessage(CliOutputVerbosity.Detailed, resolvedVerbosity, "  New jobs details:");
                                        foreach (var newJob in jobResults)
                                        {
                                            LogMessage(CliOutputVerbosity.Detailed, resolvedVerbosity, $"    - URL: {newJob.Url}");
                                        }
                                    }

                                    // Publishing is now routed through the orchestrator publish port.
                                    var publishResult = await _orchestrator.PublishArticlesAsync(new PublishArticlesRequest(jobResults, connection));
                                    if (publishResult.PublishingEnabled)
                                    {
                                        publishingEnabled = true;
                                        LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, "=== PUBLISHING NEW JOBS ===");
                                        publishedCount += publishResult.PublishedCount;
                                        if (!publishResult.Success)
                                        {
                                            publishFailure = true;
                                            failedRuns.Add($"{scrapeDate:yyyy-MM-dd} {site}: publish failed - {publishResult.ErrorMessage}");
                                        }
                                        else
                                        {
                                            LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"Published {publishResult.PublishedCount} jobs.");
                                        }
                                    }

                                    successfulRuns.Add($"{scrapeDate:yyyy-MM-dd} {site}");
                                    LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"{site} completed ({scrapeDate:yyyy-MM-dd})");
                                }
                                catch (Exception ex)
                                {
                                    failedRuns.Add($"{scrapeDate:yyyy-MM-dd} {site}: {ex.Message}");
                                    LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, $"{site} failed ({scrapeDate:yyyy-MM-dd}): {ex.Message}");
                                }

                                LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, string.Empty);
                            }
                        }

                        // Summary
                        LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, "=== SUMMARY ===");
                        if (successfulRuns.Any())
                        {
                            LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, $"Successful runs ({successfulRuns.Count}):");
                            foreach (var success in successfulRuns)
                            {
                                LogMessage(CliOutputVerbosity.Normal, resolvedVerbosity, $"  - {success}");
                            }
                        }
                        if (failedRuns.Any())
                        {
                            LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, $"Failed runs ({failedRuns.Count}):");
                            foreach (var failure in failedRuns)
                            {
                                LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, $"  - {failure}");
                            }
                        }
                        if (publishingEnabled)
                        {
                            LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, $"Published jobs: {publishedCount}");
                        }

                        if (publishFailure)
                        {
                            LogMessage(CliOutputVerbosity.Quiet, resolvedVerbosity, "Command failed because one or more publish operations failed.");
                            Environment.ExitCode = 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during archive scraping: {ex.Message}");
                        Environment.ExitCode = 1;
                    }
                },
                dateOption,
                fromOption,
                toOption,
                newsiteOption,
                feedConfigOption,
                connectionOption,
                verboseOption,
                verbosityOption);

        rootCommand.Add(scrapeArchiveCommand);
        return scrapeArchiveCommand;
    }

    private static void LogMessage(CliOutputVerbosity requiredLevel, CliOutputVerbosity currentLevel, string message)
    {
        if (currentLevel < requiredLevel)
        {
            return;
        }

        Console.WriteLine(message);
    }
}
