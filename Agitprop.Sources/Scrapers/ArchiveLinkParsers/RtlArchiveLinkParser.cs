using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchiveLinkParsers;

public class RtlArchiveLinkParser : ILinkParser
{
    private readonly Uri baseUri = new Uri("https://rtl.hu");

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, string docString)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(docString);
        return GetLinksAsync(baseUrl, doc);
    }

    public Task<List<ScrapingJobDescription>> GetLinksAsync(string baseUrl, HtmlDocument doc)
    {
        var jobs = doc.DocumentNode.SelectNodes("//article").Select(x => x.FirstChild.GetAttributeValue("href", ""))
                                   .Select(link => new NewsfeedJobDescrpition
                                   {
                                       Url = new Uri(baseUri, link).ToString(),
                                       Type = PageContentType.Article,
                                   })
                                   .Cast<ScrapingJobDescription>()
                                   .ToList();

        return Task.FromResult(jobs);
    }
}
