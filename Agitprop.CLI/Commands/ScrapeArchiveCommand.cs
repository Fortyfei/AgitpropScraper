using System.CommandLine;
using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.PageRequester;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using System.Net;

namespace Agitprop.CLI.Commands;

public static class ScrapeArchiveCommand
{
    private static readonly string CommandName = "scrape-archive";
    private static readonly string _queueName = "newsfeed-job";


    internal static Command AddScrapeArchiveCommand(this RootCommand rootCommand)
    {
        var dateOption = new Option<string>(
            ["--date", "-d"],
            () => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            "Specifies the date for scraping archives (format: yyyy-mm-dd, default: today)");

        var newsiteOption = new Option<string[]>(
            ["--newsite", "-s"],
            "Specifies news sites to scrape (can be used multiple times, comma-separated, or 'all' for all sites)");

        var connectionOption = new Option<string>(
            ["--connection", "-c"],
            "RabbitMQ connection string for sending scraping jobs to queue");

        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            () => false,
            "Prints detailed information about the scraping process and new jobs");

        var scrapeArchiveCommand = new Command(CommandName, "Scrapes archives and lists article URLs")
        {
            dateOption,
            newsiteOption,
            connectionOption,
            verboseOption
        };

        scrapeArchiveCommand.SetHandler(async (string date, string[] newsites, string connection, bool verbose) =>
                {
                    var failedSites = new List<string>();
                    var successfulSites = new List<string>();

                    try
                    {
                        // Parse and validate date
                        if (string.IsNullOrEmpty(date) || !DateOnly.TryParse(date, out var parsedDate))
                        {
                            Console.WriteLine($"Error: Invalid date format. Use yyyy-mm-dd format.");
                            return;
                        }

                        // Parse newsites - handle both comma-separated and multiple options
                        var sites = ParseNewsites(newsites ?? Array.Empty<string>());
                        if (sites.Count == 0)
                        {
                            Console.WriteLine($"Error: No valid news sites specified.");
                            return;
                        }

                        Console.WriteLine($"Scraping archives for date: {parsedDate:yyyy-MM-dd}");
                        Console.WriteLine($"News sites: {string.Join(", ", sites.Select(s => s.ToString()))}");
                        bool haveConnection = !string.IsNullOrEmpty(connection);
                        if (verbose)
                        {
                            Console.WriteLine($"Verbose mode: enabled");
                            Console.WriteLine($"Connection string provided: {(haveConnection ? "Yes" : "No")}");
                        }
                        Console.WriteLine();
                        foreach (var site in sites)
                        {
                            try
                            {
                                Console.WriteLine($"--- Scraping {site} ---");
                                var jobResults = await ScrapeSiteArchive(site, parsedDate, connection, verbose);

                                // Publishing new jobs
                                if (haveConnection)
                                {
                                    Console.WriteLine("=== PUBLISHING NEW JOBS ===");
                                    await PublishArticlesAsJobs(jobResults, connection, verbose);
                                }

                                successfulSites.Add(site.ToString());
                                Console.WriteLine($"{site} completed");
                            }
                            catch (Exception ex)
                            {
                                failedSites.Add($"{site}: {ex.Message}");
                                Console.WriteLine($"{site} failed: {ex.Message}");
                            }
                            Console.WriteLine();
                        }

                        // Summary
                        Console.WriteLine("=== SUMMARY ===");
                        if (successfulSites.Any())
                        {
                            Console.WriteLine($"Successful sites ({successfulSites.Count}): {string.Join(", ", successfulSites)}");
                        }
                        if (failedSites.Any())
                        {
                            Console.WriteLine($"Failed sites ({failedSites.Count}):");
                            foreach (var failure in failedSites)
                            {
                                Console.WriteLine($"  - {failure}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during archive scraping: {ex.Message}");
                    }
                },
                dateOption,
                newsiteOption,
                connectionOption,
                verboseOption); rootCommand.Add(scrapeArchiveCommand);
        return scrapeArchiveCommand;
    }

    private static List<NewsSites> ParseNewsites(string[] newsites)
    {
        // If "all" is specified, return all available sites
        foreach (var site in newsites)
        {
            if (site.Trim().ToLower() == "all")
            {
                return Enum.GetValues<NewsSites>().ToList();
            }
        }

        var sites = new List<NewsSites>();

        foreach (var site in newsites)
        {
            // Handle comma-separated values
            var parts = site.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim().ToLower();
                if (Enum.TryParse<NewsSites>(trimmed, true, out var parsedSite))
                {
                    if (!sites.Contains(parsedSite))
                    {
                        sites.Add(parsedSite);
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Unknown news site '{trimmed}' will be ignored.");
                }
            }
        }

        return sites;
    }

    private static async Task<List<ScrapingJobDescription>> ScrapeSiteArchive(NewsSites site, DateOnly date, string? connectionString, bool verbose = false)
    {
        var cookiesStorage = new CookieStorage();

        // Create an empty configuration since the Spider requires it
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>());
        var configuration = configBuilder.Build();

        var spider = new Spider(
            new PageTransport(
                new HttpStaticPageLoader(new RespectfulPageRequester(new CookieContainer()), cookiesStorage),
                new PuppeteerPageLoader(cookiesStorage),
                NullLogger<PageTransport>.Instance),
            configuration);

        var job = CreateArchiveJob(date, site);
        var sink = new ArchiveSink(site, connectionString);

        var jobResults = await spider.CrawlAsync(job.ConvertToScrapingJob(), sink);
        Console.WriteLine($"Crawling finished for {job.Url}. Articles found: {jobResults.Count}");

        if (verbose && jobResults.Any())
        {
            Console.WriteLine($"  New jobs details:");
            foreach (var newJob in jobResults)
            {
                Console.WriteLine($"    - URL: {newJob.Url}");
            }
        }

        return jobResults;
    }

    private static NewsfeedJobDescrpition CreateArchiveJob(DateOnly date, NewsSites site)
    {
        string url = site switch
        {
            NewsSites.Origo => $"https://www.origo.hu/hirarchivum/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.Ripost => $"https://ripost.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.Mandiner => $"https://mandiner.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.Metropol => $"https://metropol.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.MagyarNemzet => $"https://magyarnemzet.hu/{date:yyyyMM}_sitemap.xml",
            NewsSites.PestiSracok => $"https://www.pestisracok.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.MagyarJelen => $"https://www.magyarjelen.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.HuszonnegyHu => $"https://www.24.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.NegyNegyNegy => $"https://www.444.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.HVG => $"https://www.hvg.hu/frisshirek/{date.Year:D4}.{date.Month:D2}.{date.Day:D2}",
            NewsSites.Telex => $"https://telex.hu/sitemap/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}/news.xml",
            NewsSites.Index => $"https://index.hu/sitemap/cikkek_{date:yyyyMM}.xml",
            NewsSites.Merce => $"https://www.merce.hu/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}",
            NewsSites.Kurucinfo => throw new NotImplementedException("Kurucinfo scraping by date is not supported"),
            NewsSites.Alfahir => throw new NotImplementedException("Alfahir scraping by date is not supported"),
            NewsSites.RTL => throw new NotImplementedException("RTL scraping by date is not supported"),
            _ => throw new NotImplementedException($"Scraping by date is not supported for {site}"),
        };

        return new NewsfeedJobDescrpition
        {
            Url = url,
            Type = PageContentType.Archive
        };
    }

    private static async Task PublishArticlesAsJobs(List<ScrapingJobDescription> articlesUrls, string connectionString, bool verbose = false)
    {
        try
        {
            var factory = new ConnectionFactory();
            var uri = new Uri(connectionString);
            factory.Uri = uri;

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(queue: _queueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            // For now, we'll publish the archive URL as an article job
            // In a real implementation, this would extract individual article URLs from the archive

            foreach (var archiveUrl in articlesUrls)
            {
                var job = new NewsfeedJobDescrpition
                {
                    Url = archiveUrl.Url,
                    Type = PageContentType.Article
                };

                var message = JsonSerializer.Serialize(job);
                var body = Encoding.UTF8.GetBytes(message);

                await channel.BasicPublishAsync(exchange: "",
                                               routingKey: _queueName,
                                               mandatory: true,
                                               body: body);


                if (verbose) Console.WriteLine($"  Published archive job: {archiveUrl.Url} to queue '{_queueName}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error publishing to RabbitMQ: {ex.Message}");
        }
    }

    private class ArchiveSink : Core.Interfaces.ISink
    {
        private readonly NewsSites _site;
        private readonly string? _connectionString;

        public ArchiveSink(NewsSites site, string? connectionString)
        {
            _site = site;
            _connectionString = connectionString;
        }

        public Task<bool> CheckPageAlreadyVisited(string url)
        {
            return Task.FromResult(false);
        }

        public async Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
        {
            // For now, just print the found URLs
            // In a real implementation, this would extract and list article URLs
        }

    }
}
