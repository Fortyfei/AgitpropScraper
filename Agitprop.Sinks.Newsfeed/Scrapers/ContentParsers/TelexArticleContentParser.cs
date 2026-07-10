using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class TelexArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@name='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//div[@class='title-section__top']" };
    protected override List<string> LeadXPaths => new List<string> { };
    protected override List<string> ArticleXPaths => new List<string> { "//div[contains(@class, 'article-html-content')]" };
    protected override NewsSites SourceSite => NewsSites.Telex;
}
