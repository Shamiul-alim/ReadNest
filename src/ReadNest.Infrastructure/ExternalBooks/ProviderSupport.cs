using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReadNest.Application.Abstractions;
using ReadNest.Domain.Enums;

namespace ReadNest.Infrastructure.ExternalBooks;

public sealed class GoogleBooksOptions
{
    public const string SectionName = "Catalog:GoogleBooks";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://www.googleapis.com/books/v1/";

    /// <summary>Optional. Without a key Google Books applies a lower anonymous quota.</summary>
    public string? ApiKey { get; set; }
}

public sealed class OpenLibraryOptions
{
    public const string SectionName = "Catalog:OpenLibrary";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://openlibrary.org/";

    public string CoversBaseUrl { get; set; } = "https://covers.openlibrary.org/";

    /// <summary>Open Library asks API clients to identify themselves with a contact in the User-Agent.</summary>
    public string? ContactEmail { get; set; }
}

internal static partial class ProviderSupport
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Sends a GET and maps transport, timeout, status and payload failures to <see cref="CatalogProviderException"/>.</summary>
    public static async Task<T?> GetJsonAsync<T>(HttpClient client, string relativeUrl, CatalogProvider provider, CancellationToken cancellationToken, bool notFoundAsNull = false)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new CatalogProviderException(provider, $"{provider} request failed or timed out.", ex);
        }

        using (response)
        {
            if (notFoundAsNull && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new CatalogProviderException(provider, $"{provider} rate limit reached.", isRateLimited: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogProviderException(provider, $"{provider} returned HTTP {(int)response.StatusCode}.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken)
                    ?? throw new CatalogProviderException(provider, $"{provider} returned an empty response.");
            }
            catch (JsonException ex)
            {
                throw new CatalogProviderException(provider, $"{provider} returned malformed data.", ex);
            }
        }
    }

    /// <summary>Converts provider HTML descriptions to plain text. Output is still HTML-encoded when rendered.</summary>
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreakTags().Replace(html, "\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = MultipleBlankLines().Replace(text, "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    public static int? ParseYear(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) && year is > 0 and < 3000
            ? year
            : null;

    [GeneratedRegex(@"<\s*(br|/p|/div|/li)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLines();
}
