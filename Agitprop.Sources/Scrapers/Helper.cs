using System.Net;
using System.Text.RegularExpressions;

namespace Agitprop.Sources.Scrapers;

/// <summary>
/// Provides helper methods for processing and cleaning text.
/// </summary>
internal static class Helper
{
    /// <summary>
    /// Cleans up the input text by decoding HTML, removing extra spaces, and normalizing whitespace.
    /// </summary>
    /// <param name="textIn">The input text to clean up.</param>
    /// <returns>The cleaned-up text.</returns>
    internal static string CleanUpText(string textIn)
    {
        // Decode HTML text
        var text = WebUtility.HtmlDecode(textIn);
        // Replace all new lines with space
        text = text.Replace("\n", " ").Replace("\r", " ");
        // Replace all tabs with a single space
        text = text.Replace("\t", " ");
        // Remove multiple consecutive spaces
        text = Regex.Replace(text, @"\s+", " ");
        // Trim leading and trailing spaces
        text = text.Trim();

        return text;
    }
}
