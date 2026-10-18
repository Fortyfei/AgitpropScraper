using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

internal class NegynegynegyArchiveLinkParser : ILinkParser
{
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);
    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        // Use a more robust XPath selector for 444.hu
        var articles = doc.DocumentNode.SelectNodes("//a[contains(@href, '/20')]");
        
        if (articles == null)
        {
            return Task.FromResult(new List<ScrapingJobDescription>());
        }

        var result = articles
            .Select(x => x.GetAttributeValue("href", ""))
            .Where(href => !string.IsNullOrEmpty(href) && href.Contains("/20"))
            .Select(link => new NewsfeedJobDescrpition
            {
                Url = new Uri($"https://444.hu{link}").ToString(),
                Type = PageContentType.Article,
            })
            .Cast<ScrapingJobDescription>()
            .ToList();
            
        return Task.FromResult(result);
    }
}
