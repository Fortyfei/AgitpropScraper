using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class MetropolArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string>
    {
        "//h1[contains(@class, 'article-header-title')]",
        "//h1",
        "//meta[@property='og:title']"
    };
    protected override List<string> LeadXPaths => new List<string> { "//p[contains(@class, 'article-header-lead')]" };
    protected override List<string> ArticleXPaths => new List<string> { "//metropol-wysiwyg-box" };
    protected override SourceId SourceSite => MetropolSource.SiteId;
}
