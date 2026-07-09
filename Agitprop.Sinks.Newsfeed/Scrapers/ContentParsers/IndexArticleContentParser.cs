using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class IndexArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//div[@class='content-title']" };
    protected override List<string> LeadXPaths => new List<string> { "//div[@class='lead']" };
    protected override List<string> ArticleXPaths => new List<string> { "//div[@class='cikk-torzs']/*[not(contains(@class, 'cikk-bottom-text-ad'))]" };
    protected override NewsSites SourceSite => NewsSites.Index;
}
