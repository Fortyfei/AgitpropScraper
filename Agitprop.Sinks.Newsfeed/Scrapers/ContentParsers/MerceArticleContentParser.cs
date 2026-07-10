using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class MerceArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='entry-title']" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'entry-content')]" };
    protected override NewsSites SourceSite => NewsSites.Merce;
}
