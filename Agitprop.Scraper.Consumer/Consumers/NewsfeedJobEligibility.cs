using System;
using System.Collections.Generic;
using System.IO;

using Agitprop.Core.Interfaces;
using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.Scraper.Consumer.Consumers;

internal static class NewsfeedJobEligibility
{
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".css", ".gif", ".ico", ".jpeg", ".jpg", ".js", ".pdf",
        ".png", ".svg", ".webp", ".woff", ".woff2"
    };

    internal static bool ShouldProcess(NewsfeedJobDescrpition job, INewsfeedSourceCatalog sources)
    {
        if (!Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !sources.TryGetByHost(uri.Host, out _))
        {
            return false;
        }

        return job.Type != PageContentType.Article ||
            !AssetExtensions.Contains(Path.GetExtension(uri.AbsolutePath));
    }
}
