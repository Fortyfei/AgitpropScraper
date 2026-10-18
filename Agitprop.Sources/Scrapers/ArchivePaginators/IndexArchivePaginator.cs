using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;

internal class IndexArchivePaginator : IPaginator
{
    public ScrapingJobDescription GetNextPage(string currentUrl, HtmlDocument document)
    {
        var uri = new Uri(currentUrl);
        var currentDate = DateOnly.ParseExact(uri.Segments[^1].Replace("cikkek_", "").Replace(".xml", ""), "yyyyMM");
        var nextJobDate = currentDate.AddMonths(-1);
        return new NewsfeedJobDescrpition
        {
            Url = new Uri($"{uri.GetLeftPart(UriPartial.Authority)}/sitemap/cikkek_{nextJobDate:yyyyMM}.xml").ToString(),
            Type = PageContentType.Archive,
        };
    }

    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return Task.FromResult(GetNextPage(currentUrl, doc));
    }
}
