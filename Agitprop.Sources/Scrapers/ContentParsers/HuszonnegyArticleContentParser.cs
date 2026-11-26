using Agitprop.Core.Enums;

namespace Agitprop.Sources.Scrapers.ContentParsers;

internal class HuszonnegyArticleContentParser : BaseArticleContentParser
{
    protected override List<string> DateXPaths => new List<string> { "//meta[@property='article:published_time']" };
    protected override List<string> TitleXPaths => new List<string> { "//h1[@class='o-post__title']", "//h1[contains(@class, 'o-post__title')]" };
    protected override List<string> LeadXPaths => new List<string> { "//h2[contains(@class, 'o-post__lead')]", "//h1[@class='o-post__lead lead post-lead cf _ce_measure_widget']" };
    // List items are article body too: the daily digest posts have nothing but a <ul> of headlines.
    // The "Cikkajánló" widget ("Kapcsolódó" + another article's title and lead) sits inside the same
    // body container and must stay out of the entity-recognition input, and so must sponsored boxes.
    // Plain "article_box_border" is the editorial "Keretes megjelenés" box (e.g. match data), so only
    // the sponsored variant is dropped.
    protected override List<string> ArticleXPaths => new List<string>
    {
        "//div[contains(@class, 'post-body')]" +
        "//*[self::p or self::li]" +
        "[not(ancestor::div[contains(@class, 'article_box_border_szponzibox')])" +
        " and not(ancestor::div[contains(@class, 'articRecommend')])]"
    };
    protected override SourceId SourceSite => HuszonnegyHuSource.SiteId;
}
