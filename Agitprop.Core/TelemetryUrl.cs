namespace Agitprop.Core;

public static class TelemetryUrl
{
    public static string RedactQueryAndFragment(string url)
    {
        var delimiter = url.IndexOfAny(['?', '#']);
        return delimiter < 0 ? url : url[..delimiter];
    }
}
