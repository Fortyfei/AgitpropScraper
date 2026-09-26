using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ArchiveLinkParsers;

internal class OrigoArchiveLinkParser : ILinkParser
{
    private readonly Uri baseUri = new Uri("https://www.origo.hu");

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);

    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        var hrefs = doc.DocumentNode.Descendants("article")
                   .Select(article => article.Descendants("a").FirstOrDefault())
                   .Where(a => a is not null)
                   .Select(a => a!)
                   .Select(a => a.GetAttributeValue("href", ""))
                   .ToList();
        var result = hrefs.Select(link => new Uri(baseUri, link).ToString())
                          .Select(link => new NewsfeedJobDescrpition
                          {
                              Url = new Uri(link).ToString(),
                              Type = PageContentType.Article,
                          } as ScrapingJobDescription).ToList();
        return Task.FromResult(result);
    }
}
