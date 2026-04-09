using System.Net;

namespace Agitprop.Scraper.ConsoleToolKit;

public static class ScrapeCommand
{
    private static readonly string CommandName = "scrape";

    internal static Command AddScrapeCommand(this RootCommand rootCommand)
    {
        var urlOption = new Option<string>(
            ["--url", "-u"],
            "Specifies the URL to scrape");

        var shortenOption = new Option<bool>(
            ["--shorten", "-s"],
            () => false,
            "Specifies whether to shorten the printed out info")
        {
            IsRequired = false
        };

        var addCommand = new Command(CommandName, "Scrapes and prints a website content")
        {
            urlOption,
            shortenOption,
        };
        addCommand.SetHandler(ScrapeSite, urlOption, shortenOption);

        rootCommand.Add(addCommand);
        return addCommand;
    }

    private static Task ScrapeSite(string url, bool shorten)
    {
        var cookiesStorage = new CookieStorage();

        var spider = new Spider(
            new PageTransport(
                new HttpStaticPageLoader(new RespectfulPageRequester(new CookieContainer()), cookiesStorage),
                new PuppeteerPageLoader(cookiesStorage),
                NullLogger<PageTransport>.Instance),
            null);
        var job = new NewsfeedJobDescrpition()
        {
            Type = PageContentType.Article,
            Url = url
        };
        try
        {
            spider.CrawlAsync(job.ConvertToScrapingJob(), new ConsoleSink(shorten)).Wait();
        }
        catch (Exception exp)
        {
            Console.WriteLine(exp.Message);
        }
        return Task.CompletedTask;
    }

    private class ConsoleSink : ISink
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
                string text = _shorten && result.Text.Length > 100 ? $"{result.Text[..50]}...{result.Text[^50..]}" : result.Text;
                Console.WriteLine($"Text: {text}");
                Console.WriteLine();
            }
            return Task.CompletedTask;
        }
    }
}
