using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class OrigoArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='article-title']", "//h1"     };
    protected override List<string> LeadXPaths => new List<string>
    {
        "//div[@class='article-lead']"
    };
    protected override List<string> ArticleXPaths => new List<string> { "//origo-wysiwyg-box" };
    protected override SourceId SourceSite => OrigoSource.SiteId;
    protected override bool AllowsLeadOnlyContent => true;
}
