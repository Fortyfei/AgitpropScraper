namespace Agitprop.Core;

public static class TelemetryUrl
{
    public static string RedactQueryAndFragment(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp
                || absoluteUri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(absoluteUri.Host))
        {
            return absoluteUri.GetComponents(
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.UriEscaped);
        }

        if (Uri.TryCreate(url, UriKind.Relative, out var relativeUri))
        {
            var relative = relativeUri.OriginalString;
            if (relative.StartsWith("//", StringComparison.Ordinal)
                && Uri.TryCreate($"https:{relative}", UriKind.Absolute, out var networkPathUri))
            {
                var sanitized = networkPathUri.GetComponents(
                    UriComponents.SchemeAndServer | UriComponents.Path,
                    UriFormat.UriEscaped);
                return sanitized["https:".Length..];
            }

            return RemoveQueryAndFragment(relative);
        }

        return "[invalid-url]";
    }

    /// <summary>
    /// Host of an absolute http(s) URL, or "unknown" when it cannot be parsed. Hosts are bounded by
    /// the configured sources, so they are safe trace and metric dimensions; full URLs are not.
    /// </summary>
    public static string GetServerAddress(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : "unknown";
    }

    private static string RemoveQueryAndFragment(string url)
    {
        var delimiter = url.IndexOfAny(['?', '#']);
        return delimiter < 0 ? url : url[..delimiter];
    }
}
