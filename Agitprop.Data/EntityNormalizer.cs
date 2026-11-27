using System.Text;

namespace Agitprop.Data;

/// <summary>
/// Produces the stable comparison key for entity names and aliases. NER surface forms arrive with
/// casing, spacing and punctuation noise ("ENSZ-", "Tisza-"), so comparisons use this key instead of
/// the raw text. Accents are deliberately preserved: folding them merges distinct Hungarian names.
/// </summary>
public static class EntityNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        var start = 0;
        var end = builder.Length;
        while (start < end && IsEdgeNoise(builder[start]))
        {
            start++;
        }

        while (end > start && IsEdgeNoise(builder[end - 1]))
        {
            end--;
        }

        return end <= start ? string.Empty : builder.ToString(start, end - start).ToLowerInvariant();
    }

    /// <summary>
    /// Rejects surface forms that carry no name: punctuation-only spans ("-"), pure numbers ("50")
    /// and one-character fragments. NER noise must not become an entity row.
    /// </summary>
    public static bool IsMeaningful(string normalizedName)
    {
        if (normalizedName.Length < 2)
        {
            return false;
        }

        return normalizedName.Any(char.IsLetter);
    }

    private static bool IsEdgeNoise(char character) =>
        char.IsPunctuation(character) || char.IsSymbol(character);
}
