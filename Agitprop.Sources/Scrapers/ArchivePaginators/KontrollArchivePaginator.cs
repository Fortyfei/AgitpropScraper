using Agitprop.Core;
using Agitprop.Core.Interfaces;

namespace Agitprop.Sources.Scrapers.ArchivePaginators;

internal class KontrollArchivePaginator : IPaginator
{
    public Task<ScrapingJobDescription> GetNextPageAsync(string currentUrl, string docString) =>
        throw new NotSupportedException("kontroll.hu only publishes a single rolling sitemap; there is no older archive page.");
}
