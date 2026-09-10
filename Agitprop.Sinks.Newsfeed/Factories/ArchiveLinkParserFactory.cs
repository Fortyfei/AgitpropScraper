using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed.Scrapers.ArchiveLinkParsers;

namespace Agitprop.Sinks.Newsfeed.Factories;

/// <summary>
/// Provides a factory for creating archive link parsers for different news sites.
/// </summary>
internal static class ArchiveLinkParserFactory
{
    /// <summary>
    /// Gets the archive link parser for the specified news site.
    /// </summary>
    /// <param name="siteIn">The news site for which to get the archive link parser.</param>
    /// <returns>An instance of <see cref="ILinkParser"/> for the specified news site.</returns>
    public static ILinkParser GetLinkParser(NewsSites siteIn)
    {
        return siteIn switch
        {
            NewsSites.Origo => new OrigoArchiveLinkParser(),
            NewsSites.Ripost => new RipostArchiveLinkParser(),
            NewsSites.Mandiner => new MandinerArchiveLinkParser(),
            NewsSites.Metropol => new MetropolArchiveLinkParser(),
            NewsSites.MagyarNemzet => new MagyarNemzetArchiveLinkParser(),
            NewsSites.PestiSracok => new PestiSracokArchiveLinkParser(),
            NewsSites.MagyarJelen => new MagyarJelenArchiveLinkParser(),
            NewsSites.Alfahir => new AlfahirArchiveLinkParser(),
            NewsSites.HuszonnegyHu => new HuszonnegyArchiveLinkParser(),
            NewsSites.NegyNegyNegy => new NegynegynegyArchiveLinkParser(),
            NewsSites.HVG => new HvgArchiveLinkParser(),
            NewsSites.Telex => new TelexArchiveLinkParser(),
            NewsSites.RTL => new RtlArchiveLinkParser(),
            NewsSites.Index => new IndexArchiveLinkParser(),
            NewsSites.Merce => new MerceArchiveLinkParser(),
            _ => throw new ArgumentException($"Not supported news source: {siteIn}"),
        };
    }
}
