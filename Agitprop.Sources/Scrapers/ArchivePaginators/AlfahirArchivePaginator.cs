using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;

internal class AlfahirArchivePaginator : IPaginator
{
    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, HtmlDocument document)
    {
        var url = "https://alfahir.hu/hirek/oldalak/1";
        if (int.TryParse(new Uri(currentUrl).Segments.Last(), out var counter))
        {
            url = $"https://alfahir.hu/hirek/oldalak/{++counter}";
        }
        var result = new NewsfeedJobDescrpition
        {
            Url = new Uri(url).ToString(),
            Type = PageContentType.Archive,
        } as ScrapingJobDescription;
        return Task.FromResult(result);
    }

    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
    {
        var url = "https://alfahir.hu/hirek/oldalak/1";
        if (int.TryParse(new Uri(currentUrl).Segments.Last(), out var counter))
        {
            url = $"https://alfahir.hu/hirek/oldalak/{++counter}";
        }
        var result = new NewsfeedJobDescrpition
        {
            Url = new Uri(url).ToString(),
            Type = PageContentType.Archive,
        } as ScrapingJobDescription;
        return Task.FromResult(result);
    }
}
