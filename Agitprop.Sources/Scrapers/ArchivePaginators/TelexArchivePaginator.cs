using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;
internal class TelexArchivePaginator : IPaginator
{
    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, HtmlDocument document)
    {

        if (string.IsNullOrWhiteSpace(currentUrl))
            throw new ArgumentException("Current URL cannot be null or empty.", nameof(currentUrl));

        // Parse the current URL to extract date components
        var uri = new Uri(currentUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 4 || !DateTime.TryParse($"{segments[1]}-{segments[2]}-{segments[3]}", out var currentDate))
            throw new ArgumentException("The URL does not match the expected date format.");

        // Increment the date
        var nextDate = currentDate.AddDays(1);

        // Construct the next URL
        var nextUrl = $"{uri.Scheme}://{uri.Host}/sitemap/{nextDate:yyyy/MM/dd}/news.xml";

        return Task.FromResult(new NewsfeedJobDescrpition
        {
            Url = new Uri(nextUrl).ToString(),
            Type = PageContentType.Archive,
        } as ScrapingJobDescription);


    }

    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(docString);
        return GetNextPageAsync(currentUrl, doc);
    }
}
