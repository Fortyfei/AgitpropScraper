using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ArchiveLinkParsers;

internal class MandinerArchiveLinkParser : SitemapLinkParser, ILinkParser
{
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        var result = GetLinks(docString).Select(link => new NewsfeedJobDescrpition
        {
            Url = new Uri(link).ToString(),
            Type = PageContentType.Article,

        }).Cast<ScrapingJobDescription>().ToList();
        return Task.FromResult(result);
    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        return GetLinksAsync(baseUrl, doc.DocumentNode.OuterHtml);
    }
}
