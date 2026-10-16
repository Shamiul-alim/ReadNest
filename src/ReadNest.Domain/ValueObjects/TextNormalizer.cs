using System.Globalization;
using System.Text;

namespace ReadNest.Domain.ValueObjects;

/// <summary>Produces comparison keys for names and titles (case-, accent-, punctuation- and whitespace-insensitive).</summary>
public static class TextNormalizer
{
    public static string Key(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Trims and collapses internal whitespace for display values.</summary>
    public static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
