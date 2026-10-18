using Agitprop.Core.Enums;

namespace Agitprop.Core.Interfaces;

/// <summary>
/// Describes how a single news site is scraped. One implementation per site.
/// </summary>
public interface INewsfeedSource
{
    SourceId Id { get; }

    /// <summary>Hosts whose article and archive URLs belong to this source (lowercase).</summary>
    IReadOnlyCollection<string> Hosts { get; }

    /// <summary>Extra hosts that only appear in feed configuration (e.g. RSS endpoints).</summary>
    IReadOnlyCollection<string> FeedHosts => [];

    IContentParser CreateContentParser();

    ILinkParser CreateLinkParser();

    IPaginator CreatePaginator();

    PageType ArchivePageType => PageType.Static;

    List<PageAction>? CreateArchiveActions() => null;

    /// <summary>The archive URL for a day, or null when the site has no date-based archive.</summary>
    string? GetArchiveUrl(DateOnly date) => null;
}
