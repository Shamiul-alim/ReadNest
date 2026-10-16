using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Infrastructure.ExternalBooks;

/// <summary>Google Books API v1 (https://developers.google.com/books/docs/v1/using). An API key is optional.</summary>
public sealed partial class GoogleBooksProvider(HttpClient httpClient, IOptions<GoogleBooksOptions> options) : IBookCatalogProvider
{
    public CatalogProvider Provider => CatalogProvider.GoogleBooks;

    public bool IsEnabled => options.Value.Enabled;

    public async Task<CatalogProviderPage> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var url = new StringBuilder("volumes?q=")
            .Append(Uri.EscapeDataString(BuildQuery(request)))
            .Append(CultureInfo.InvariantCulture, $"&startIndex={(request.Page - 1) * request.PageSize}")
            .Append(CultureInfo.InvariantCulture, $"&maxResults={request.PageSize}")
            .Append("&printType=books");
        if (request.Language is { } language)
        {
            url.Append("&langRestrict=").Append(Uri.EscapeDataString(language));
        }

        AppendKey(url);
        var response = await ProviderSupport.GetJsonAsync<VolumesResponse>(httpClient, url.ToString(), Provider, cancellationToken);
        var items = (response?.Items ?? []).Select(Map).OfType<CatalogBook>().ToList();
        return new CatalogProviderPage(items, response?.TotalItems);
    }

    public async Task<CatalogBook?> GetByIdAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!VolumeIdRegex().IsMatch(externalId))
        {
            return null;
        }

        var url = new StringBuilder("volumes/").Append(Uri.EscapeDataString(externalId)).Append('?');
        AppendKey(url);
        var volume = await ProviderSupport.GetJsonAsync<Volume>(httpClient, url.ToString().TrimEnd('?', '&'), Provider, cancellationToken, notFoundAsNull: true);
        return volume is null ? null : Map(volume);
    }

    internal static string BuildQuery(CatalogSearchRequest request)
    {
        var term = request.Query.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
        return request.Field switch
        {
            CatalogSearchField.Title => $"intitle:\"{term}\"",
            CatalogSearchField.Author => $"inauthor:\"{term}\"",
            CatalogSearchField.Isbn => $"isbn:{term}",
            CatalogSearchField.Subject => $"subject:\"{term}\"",
            _ => term,
        };
    }

    internal static CatalogBook? Map(Volume volume)
    {
        var info = volume.VolumeInfo;
        if (string.IsNullOrWhiteSpace(volume.Id) || info is null || string.IsNullOrWhiteSpace(info.Title))
        {
            return null;
        }

        string? isbn10 = null;
        string? isbn13 = null;
        foreach (var identifier in info.IndustryIdentifiers ?? [])
        {
            var value = Isbn.Clean(identifier.Identifier);
            if (identifier.Type == "ISBN_13" && Isbn.IsValidIsbn13(value))
            {
                isbn13 ??= value;
            }
            else if (identifier.Type == "ISBN_10" && Isbn.IsValidIsbn10(value))
            {
                isbn10 ??= value;
            }
        }

        var cover = info.ImageLinks?.Thumbnail ?? info.ImageLinks?.SmallThumbnail;
        cover = cover?.Replace("&edge=curl", string.Empty, StringComparison.Ordinal);

        return new CatalogBook(
            CatalogProvider.GoogleBooks,
            volume.Id,
            info.Title.Trim(),
            string.IsNullOrWhiteSpace(info.Subtitle) ? null : info.Subtitle.Trim(),
            info.Authors ?? [],
            ProviderSupport.ToPlainText(info.Description),
            isbn10,
            isbn13,
            info.PageCount is > 0 ? info.PageCount : null,
            info.Language,
            info.PublishedDate,
            ProviderSupport.ParseYear(info.PublishedDate),
            info.Publisher,
            Application.Common.InputSanitizer.SafeExternalUrl(cover),
            Application.Common.InputSanitizer.SafeExternalUrl(info.CanonicalVolumeLink ?? info.InfoLink),
            info.Categories ?? []);
    }

    private void AppendKey(StringBuilder url)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            url.Append("&key=").Append(Uri.EscapeDataString(options.Value.ApiKey));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex VolumeIdRegex();

    internal sealed class VolumesResponse
    {
        public int? TotalItems { get; set; }

        public List<Volume>? Items { get; set; }
    }

    internal sealed class Volume
    {
        public string? Id { get; set; }

        public VolumeInfo? VolumeInfo { get; set; }
    }

    internal sealed class VolumeInfo
    {
        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        public List<string>? Authors { get; set; }

        public string? Publisher { get; set; }

        public string? PublishedDate { get; set; }

        public string? Description { get; set; }

        public List<IndustryIdentifier>? IndustryIdentifiers { get; set; }

        public int? PageCount { get; set; }

        public List<string>? Categories { get; set; }

        public ImageLinks? ImageLinks { get; set; }

        public string? Language { get; set; }

        public string? InfoLink { get; set; }

        public string? CanonicalVolumeLink { get; set; }
    }

    internal sealed class IndustryIdentifier
    {
        public string? Type { get; set; }

        public string? Identifier { get; set; }
    }

    internal sealed class ImageLinks
    {
        public string? SmallThumbnail { get; set; }

        public string? Thumbnail { get; set; }
    }
}
