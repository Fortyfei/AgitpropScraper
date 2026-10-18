using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

internal class HvgArchiveLinkParser : ILinkParser
{
    private readonly Uri baseUri = new Uri("https://www.hvg.hu");

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);
    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        //*[@id="latestListContent"]/article[157]/div/h1/a
        var articleUrls = doc.DocumentNode.SelectNodes("//*[@id='latestListContent']/article/div/h1/a").ToList();
        var idk=articleUrls .Select(x => x.GetAttributeValue("href", "")).ToList();
        var result = idk.Select(link => new NewsfeedJobDescrpition
        {
            Url = new Uri(baseUri, link).ToString(),
            Type = PageContentType.Article,
        }).Cast<ScrapingJobDescription>().ToList();
        return Task.FromResult(result);
    }
}
