using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;

internal class HuszonnegyArchivePaginator : DateBasedArchive, IPaginator
{
    public async Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, HtmlDocument document)
    {
        var url = GetDateBasedUrl("https://24.hu", currentUrl);
        var job = new NewsfeedJobDescrpition
        {
            Url = new Uri(url).ToString(),
            Type = PageContentType.Archive,
        };
        return await Task.FromResult(job);
    }

    public async Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(docString);
        return await GetNextPageAsync(currentUrl, doc);
    }
}
