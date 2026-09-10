using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class HvgArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']", "//time[@datetime]" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[contains(@class, 'title')]", "//div[@class='article-title article-title']" };
    protected override List<string> LeadXPaths => new List<string> { "//p[contains(@class, 'article-lead') or contains(@class, 'entry-summary') or contains(@class, 'lead') ]", "//meta[@name='description']" };
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//div[@id='free-body']//*[self::p or self::blockquote[not(contains(@class,'twitter-tweet'))]]",
        "//div[contains(@class, 'article-content') and contains(@class, 'entry-content')]//*[self::p or self::blockquote]",
        "//div[@class='article-body']//*[self::p or self::blockquote]",
        "//div[contains(@class, 'article-content')]//*[self::p or self::blockquote]"
    };
    protected override NewsSites SourceSite => NewsSites.HVG;
}
