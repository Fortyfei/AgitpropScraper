using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

internal class HuszonnegyArchiveLinkParser : ILinkParser
{
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes("//*[@id='content']/h2/a");
        var result = nodes.Select(x => x.GetAttributeValue("href", ""))
                          .Select(static url => new NewsfeedJobDescrpition
                          {
                              Url = new Uri(url).ToString(),
                              Type = PageContentType.Article,
                          }).Cast<ScrapingJobDescription>().ToList();
        return Task.FromResult(result);
    }
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);
    }
}
