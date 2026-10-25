using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class MagyarNemzetArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string>
    {
        "//h1[@class='title']",
        "//h1[contains(@class, 'article-title')]",
        "//h1",
        "//meta[@property='og:title']"
    };
    protected override List<string> LeadXPaths => new List<string>
    {
        "//h2[@class='lead']",
        "//meta[@name='description']"
    };
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//script[@class='structured-data' and contains(text(), '\"articleBody\"')]",
        "//app-article-text//div[contains(@class, 'article-text-formatter')]//*[self::p or self::blockquote]",
        "//div[contains(@class, 'article-text-formatter')]//*[self::p or self::blockquote]"
    };
    protected override SourceId SourceSite => MagyarNemzetSource.SiteId;
    protected override bool AllowsLeadOnlyContent => true;
}
