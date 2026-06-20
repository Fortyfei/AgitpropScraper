using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers;

namespace Agitprop.Sinks.Newsfeed.Factories;

/// <summary>
/// Provides a factory for creating content parsers for different news sites.
/// </summary>
internal static class ContentParserFactory
{
    /// <summary>
    /// Gets the content parser for the specified news site.
    /// </summary>
    /// <param name="siteIn">The news site for which to get the content parser.</param>
    /// <returns>An instance of <see cref="IContentParser"/> for the specified news site.</returns>
    public static IContentParser GetContentParser(NewsSites siteIn)
    {
        return siteIn switch
        {
            NewsSites.Origo => new OrigoArticleContentParser(),
            NewsSites.Ripost => new RipostArticleContentParser(),
            NewsSites.Mandiner => new MandinerArticleContentParser(),
            NewsSites.Metropol => new MetropolArticleContentParser(),
            NewsSites.MagyarNemzet => new MagyarNemzetArticleContentParser(),
            NewsSites.PestiSracok => new PestiSracokArticleContentParser(),
            NewsSites.MagyarJelen => new MagyarJelenArticleContentParser(),
            NewsSites.Alfahir => new AlfahirArticleContentParser(),
            NewsSites.HuszonnegyHu => new HuszonnegyArticleContentParser(),
            NewsSites.NegyNegyNegy => new NegynegynegyArticleContentParser(),
            NewsSites.HVG => new HvgArticleContentParser(),
            NewsSites.Telex => new TelexArticleContentParser(),
            NewsSites.RTL => new RtlArticleContentParser(),
            NewsSites.Index => new IndexArticleContentParser(),
            NewsSites.Merce => new MerceArticleContentParser(),
            _ => throw new ArgumentException($"Not supported news source: {siteIn}"),
        };
    }
}
