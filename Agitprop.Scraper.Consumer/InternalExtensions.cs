using System;

namespace Agitprop.Scraper.Consumer;

internal static class InternalExtensions
{
    internal static string GetExceptionMessage(Exception ex)
    {
        var message = ex.Message?.Trim() ?? string.Empty;
        if (message.Length > 256)
        {
            return message[..256] + "...";
        }

        return message.Length == 0 ? "<empty>" : message;
    }

    internal static string GetDomainFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "invalid_url";
    }
}
