using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

internal class MerceArchiveLinkParser : ILinkParser
{
    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);

        return GetLinksAsync(baseUrl, doc);

    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        HtmlNodeCollection articles = doc.DocumentNode.SelectNodes("//article/a");
        var result = articles.Select(x => x.GetAttributeValue("href", ""))
                             .Select(link => new NewsfeedJobDescrpition
                             {
                                 Url = new Uri(link).ToString(),
                                 Type = PageContentType.Article,

                             }).Cast<ScrapingJobDescription>().ToList();
        return Task.FromResult(result);
    }
}
