using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

internal class BlikkArchiveLinkParser : ILinkParser
{
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);
    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        var anchors = doc.DocumentNode.SelectNodes("//section[contains(@class,'archive')]//li/a[@href]");
        var result = (anchors ?? Enumerable.Empty<HtmlNode>())
            .Select(x => x.GetAttributeValue("href", ""))
            .Select(href => Uri.TryCreate(href, UriKind.Absolute, out var abs) ? abs.ToString() : new Uri(new Uri(baseUrl), href).ToString())
            .Distinct()
            .Select(link => new NewsfeedJobDescrpition
            {
                Url = link,
                Type = PageContentType.Article,
            }).Cast<ScrapingJobDescription>().ToList();
        return Task.FromResult(result);
    }
}
