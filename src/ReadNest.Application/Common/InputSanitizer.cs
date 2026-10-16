using System.Security.Cryptography;

namespace ReadNest.Application.Common;

public static class InputSanitizer
{
    public const int MaxSearchLength = 200;

    /// <summary>Trims a free-text search term; returns null when empty and rejects overly long input.</summary>
    public static string? SearchTerm(string? value, string field = "search")
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxSearchLength)
        {
            throw new RequestValidationException(field, $"Search text must be at most {MaxSearchLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Accepts only absolute http(s) URLs without embedded credentials and returns them as https.
    /// Anything else (javascript:, data:, relative paths, overly long values) yields null.
    /// </summary>
    public static string? SafeExternalUrl(string? value, int maxLength = 1000)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>Like <see cref="SafeExternalUrl"/> but raises a validation error for unacceptable non-empty input.</summary>
    public static string? RequireSafeUrlOrNull(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return SafeExternalUrl(value)
            ?? throw new RequestValidationException(field, "Enter a valid https:// URL (maximum 1000 characters).");
    }

    /// <summary>URL-safe random token, e.g. for public share links.</summary>
    public static string RandomToken(int bytes = 12) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
