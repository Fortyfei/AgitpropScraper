using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class BlikkArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1" };
    protected override List<string> LeadXPaths => new List<string> { "//section[@id='lead']" };
    protected override List<string> ArticleXPaths => new List<string> { "//section[@id='article-content']" };
    protected override SourceId SourceSite => BlikkSource.SiteId;
}
