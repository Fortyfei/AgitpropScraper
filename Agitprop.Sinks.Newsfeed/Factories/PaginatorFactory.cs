using Agitprop.Core.Enums;
using Agitprop.Core.Interfaces;
using Agitprop.Sinks.Newsfeed.Scrapers.ArchivePaginators;

namespace Agitprop.Sinks.Newsfeed.Factories;

/// <summary>
/// Provides a factory for creating paginator instances for different news sites.
/// </summary>
internal static class PaginatorFactory
{
    /// <summary>
    /// Gets the paginator for the specified news site.
    /// </summary>
    /// <param name="source">The news site for which to get the paginator.</param>
    /// <returns>An instance of <see cref="IPaginator"/> for the specified news site.</returns>
    public static IPaginator GetPaginator(NewsSites source)
    {
        return source switch
        {
            NewsSites.Origo => new OrigoArchivePaginator(),
            NewsSites.Ripost => new RipostArchivePaginator(),
            NewsSites.Mandiner => new MandinerArchivePaginator(),
            NewsSites.Metropol => new MetropolArchivePaginator(),
            NewsSites.MagyarNemzet => new MagyarNemzetArchivePaginator(),
            NewsSites.PestiSracok => new PestiSracokArchivePaginator(),
            NewsSites.MagyarJelen => new MagyarJelenArchivePaginator(),
            NewsSites.Alfahir => new AlfahirArchivePaginator(),
            NewsSites.HuszonnegyHu => new HuszonnegyArchivePaginator(),
            NewsSites.NegyNegyNegy => new NegynegynegyArchivePaginator(),
            NewsSites.HVG => new HvgArchivePaginator(),
            NewsSites.Telex => new TelexArchivePaginator(),
            NewsSites.RTL => new RtlArchivePaginator(),
            NewsSites.Index => new IndexArchivePaginator(),
            NewsSites.Merce => new MerceArchivePaginator(),
            _ => throw new ArgumentException($"Not supported news source: {source}"),
        };
    }
}
