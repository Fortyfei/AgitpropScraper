using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

// TODO: Parser broken as of 2026-07-10 - site structure may require manual inspection.
internal class MagyarNemzetArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='title']", "//h1[contains(@class, 'article-title')]" };
    protected override List<string> LeadXPaths => new List<string> { "//h2[@class='lead']" };
    protected override List<string> ArticleXPaths => new List<string> { "//app-article-text", "//div[contains(@class, 'article-text-formatter')]" };
    protected override NewsSites SourceSite => NewsSites.MagyarNemzet;
}
