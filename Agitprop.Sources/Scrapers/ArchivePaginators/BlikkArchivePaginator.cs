using System.Net;

using Agitprop.Core;
using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;

using HtmlAgilityPack;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;

internal class BlikkArchivePaginator : IPaginator
{
    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(docString);

        var current = new Uri(currentUrl);
        var nextPage = doc.DocumentNode
            .SelectSingleNode("//a[contains(normalize-space(.), 'Következő oldal')]")
            ?.GetAttributeValue("href", "");

        string url;
        if (!string.IsNullOrWhiteSpace(nextPage))
        {
            url = new Uri(current, WebUtility.HtmlDecode(nextPage)).ToString();
        }
        else
        {
            var query = System.Web.HttpUtility.ParseQueryString(current.Query);
            var date = DateOnly.TryParse(query["date"], out var parsed)
                ? parsed
                : DateOnly.FromDateTime(DateTime.Today);
            url = $"{current.GetLeftPart(UriPartial.Path)}?date={date.AddDays(-1):yyyy-MM-dd}";
        }

        return Task.FromResult<ScrapingJobDescription>(new NewsfeedJobDescrpition
        {
            Url = url,
            Type = PageContentType.Archive,
        });
    }
}
