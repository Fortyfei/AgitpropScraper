using System.CommandLine;
using Agitprop.CLI.Services;

namespace Agitprop.CLI.Commands;

public static class ScrapeArticleCommand
{
    private static readonly string CommandName = "scrape-article";
    private static readonly IScrapeCommandOrchestrator _orchestrator = new ScrapeCommandOrchestrator();

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
        var result = await _orchestrator.ExecuteArticleAsync(new ArticleScrapeRequest(url, shorten));

        foreach (var line in result.OutputLines)
        {
            Console.WriteLine(line);
        }
    }
}
