using Agitprop.Core.Enums;

namespace Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

internal class MandinerArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='article-page-title']", "//h1[contains(@class, 'article-title')]" };
    protected override List<string> LeadXPaths => new List<string> { "//p[@class='article-page-lead']", "//p[contains(@class, 'article-lead')]" };
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//man-wysiwyg-box//div[contains(@class, 'block-content')]//*[self::p or self::blockquote]",
        "//div[contains(@class, 'block-content')]//*[self::p or self::blockquote]"
    };
    protected override NewsSites SourceSite => NewsSites.Mandiner;
}
