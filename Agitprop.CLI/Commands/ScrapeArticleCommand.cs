using System.CommandLine;

using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Infrastructure;
using Agitprop.Infrastructure.PageLoader;
using Agitprop.Infrastructure.PageRequester;
using Agitprop.Infrastructure.Puppeteer;
using Agitprop.Sinks.Newsfeed;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agitprop.CLI.Commands;

public static class ScrapeArticleCommand
{
    private static readonly string CommandName = "scrape-article";

    internal static Command AddScrapeArticleCommand(this RootCommand rootCommand)
    {
        var urlOption = new Option<string>(
            ["--url", "-u"],
            "Specifies the URL to scrape");

        var shortenOption = new Option<bool>(
            ["--shorten", "-s"],
            () => false,
            "Shortens the printed output");

        var scrapeArticleCommand = new Command(CommandName, "Scrapes a single article and prints to console")
        {
            urlOption,
            shortenOption
        };

        scrapeArticleCommand.SetHandler(async (url, shorten) =>
        {
            try
            {
                await ScrapeSingleArticle(url, shorten);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during scraping: {ex.Message}");
            }
        }, 
        urlOption, 
        shortenOption);

        rootCommand.Add(scrapeArticleCommand);
        return scrapeArticleCommand;
    }

    private static async Task ScrapeSingleArticle(string url, bool shorten)
    {
        Console.WriteLine($"Scraping single article: {url}");
        
        var cookiesStorage = new CookieStorage();
        
        // Create an empty configuration since the Spider requires it
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>());
        var configuration = configBuilder.Build();

        var spider = new Spider(
            new PageTransport(
                new HttpStaticPageLoader(new RespectfulPageRequester(new System.Net.CookieContainer()), cookiesStorage),
                new PuppeteerPageLoader(cookiesStorage),
                NullLogger<PageTransport>.Instance),
            configuration);

        var job = new NewsfeedJobDescrpition()
        {
            Type = PageContentType.Article,
            Url = url
        };

        var sink = new ConsoleSink(shorten);
        spider.CrawlAsync(job.ConvertToScrapingJob(), sink).Wait();
    }

    private class ConsoleSink : Agitprop.Core.Interfaces.ISink
    {
        private readonly bool _shorten;

        public ConsoleSink(bool shorten)
        {
            _shorten = shorten;
        }

        public Task<bool> CheckPageAlreadyVisited(string url)
        {
            return Task.FromResult(false);
        }

        public Task EmitAsync(string url, List<ContentParserResult> data, CancellationToken cancellationToken = default)
        {
            foreach (var result in data)
            {
                Console.WriteLine($"Source: {url}");
                Console.WriteLine($"SourceSite: {result.SourceSite}");
                Console.WriteLine($"PublishDate: {result.PublishDate}");
                string text = _shorten && result.Text.Length > 100 
                    ? $"{result.Text[..50]}...{result.Text[^50..]}" 
                    : result.Text;
                Console.WriteLine($"Text: {text}");
                Console.WriteLine();
            }
            return Task.CompletedTask;
        }
    }
}
