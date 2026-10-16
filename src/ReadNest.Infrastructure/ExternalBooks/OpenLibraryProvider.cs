using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Infrastructure.ExternalBooks;

/// <summary>
/// Open Library (https://openlibrary.org/developers/api). Search uses search.json; details combine the work,
/// its authors and its first editions. Covers come from covers.openlibrary.org.
/// </summary>
public sealed partial class OpenLibraryProvider(HttpClient httpClient, IOptions<OpenLibraryOptions> options) : IBookCatalogProvider
{
    private const string SearchFields = "key,title,subtitle,author_name,isbn,number_of_pages_median,language,first_publish_year,publisher,cover_i,subject";

    private static readonly Dictionary<string, string> TwoToThree = new(StringComparer.Ordinal)
    {
        ["en"] = "eng", ["fr"] = "fre", ["de"] = "ger", ["es"] = "spa", ["it"] = "ita", ["pt"] = "por", ["nl"] = "dut",
        ["ru"] = "rus", ["ja"] = "jpn", ["zh"] = "chi", ["ar"] = "ara", ["fa"] = "per", ["tr"] = "tur", ["pl"] = "pol",
        ["sv"] = "swe", ["ko"] = "kor", ["hi"] = "hin", ["he"] = "heb", ["el"] = "gre", ["da"] = "dan", ["fi"] = "fin",
        ["no"] = "nor", ["cs"] = "cze", ["uk"] = "ukr",
    };

    private static readonly Dictionary<string, string> ThreeToTwo = TwoToThree.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public CatalogProvider Provider => CatalogProvider.OpenLibrary;

    public bool IsEnabled => options.Value.Enabled;

    public async Task<CatalogProviderPage> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var parameter = request.Field switch
        {
            CatalogSearchField.Title => "title",
            CatalogSearchField.Author => "author",
            CatalogSearchField.Isbn => "isbn",
            CatalogSearchField.Subject => "subject",
            _ => "q",
        };
        var url = new StringBuilder("search.json?")
            .Append(parameter).Append('=').Append(Uri.EscapeDataString(request.Query))
            .Append(CultureInfo.InvariantCulture, $"&page={request.Page}&limit={request.PageSize}")
            .Append("&fields=").Append(SearchFields);
        if (request.Language is { } language && TwoToThree.TryGetValue(language, out var code))
        {
            url.Append("&language=").Append(code);
        }

        var response = await ProviderSupport.GetJsonAsync<SearchResponse>(httpClient, url.ToString(), Provider, cancellationToken);
        var preferredIsbn = request.Field == CatalogSearchField.Isbn ? request.Query : null;
        var items = (response?.Docs ?? []).Select(d => Map(d, preferredIsbn)).OfType<CatalogBook>().ToList();
        return new CatalogProviderPage(items, response?.NumFound);
    }

    /// <summary>
    /// Combines the work record (description, subjects, covers) with the work's search document (authors,
    /// representative ISBN, median page count, first publication year) so details match the search results.
    /// </summary>
    public async Task<CatalogBook?> GetByIdAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!WorkIdRegex().IsMatch(externalId))
        {
            return null;
        }

        var work = await ProviderSupport.GetJsonAsync<Work>(httpClient, $"works/{externalId}.json", Provider, cancellationToken, notFoundAsNull: true);
        if (work is null || string.IsNullOrWhiteSpace(work.Title))
        {
            return null;
        }

        var search = await ProviderSupport.GetJsonAsync<SearchResponse>(
            httpClient,
            $"search.json?q={Uri.EscapeDataString("key:/works/" + externalId)}&limit=1&fields={SearchFields}",
            Provider,
            cancellationToken);
        var fromSearch = search?.Docs?.FirstOrDefault() is { } doc ? Map(doc, preferredIsbn: null) : null;

        var authors = fromSearch?.Authors ?? await LoadAuthorsAsync(work, cancellationToken);
        var cover = work.Covers?.FirstOrDefault(c => c > 0);
        var subjects = (work.Subjects ?? []).Take(20).ToList();

        return new CatalogBook(
            CatalogProvider.OpenLibrary,
            externalId,
            work.Title.Trim(),
            string.IsNullOrWhiteSpace(work.Subtitle) ? fromSearch?.Subtitle : work.Subtitle.Trim(),
            authors,
            ProviderSupport.ToPlainText(ReadText(work.Description)),
            fromSearch?.Isbn10,
            fromSearch?.Isbn13,
            fromSearch?.PageCount,
            fromSearch?.Language,
            fromSearch?.PublishedDate ?? work.FirstPublishDate,
            fromSearch?.PublishedYear ?? ProviderSupport.ParseYear(work.FirstPublishDate) ?? ParseAnyYear(work.FirstPublishDate),
            fromSearch?.Publisher,
            cover is { } coverId ? CoverUrl(coverId) : fromSearch?.CoverUrl,
            $"https://openlibrary.org/works/{externalId}",
            subjects.Count > 0 ? subjects : fromSearch?.Categories ?? []);
    }

    private async Task<IReadOnlyList<string>> LoadAuthorsAsync(Work work, CancellationToken cancellationToken)
    {
        var keys = (work.Authors ?? [])
            .Select(a => a.Author?.Key)
            .OfType<string>()
            .Where(k => AuthorKeyRegex().IsMatch(k))
            .Take(5)
            .ToList();
        var records = await Task.WhenAll(keys.Select(key =>
            ProviderSupport.GetJsonAsync<AuthorRecord>(httpClient, $"{key.TrimStart('/')}.json", Provider, cancellationToken, notFoundAsNull: true)));
        return records.Select(a => a?.Name).OfType<string>().ToList();
    }

    internal CatalogBook? Map(SearchDoc doc, string? preferredIsbn)
    {
        var workId = doc.Key?.Split('/').LastOrDefault();
        if (workId is null || !WorkIdRegex().IsMatch(workId) || string.IsNullOrWhiteSpace(doc.Title))
        {
            return null;
        }

        var isbns = (doc.Isbn ?? []).Take(200).Select(Isbn.Clean).OfType<string>().ToList();
        var isbn13 = preferredIsbn is not null && isbns.Contains(preferredIsbn)
            ? preferredIsbn
            : isbns.FirstOrDefault(i => i.StartsWith("978", StringComparison.Ordinal) && Isbn.IsValidIsbn13(i)) ?? isbns.FirstOrDefault(Isbn.IsValidIsbn13);
        var isbn10 = Isbn.ToIsbn10(isbn13) ?? isbns.FirstOrDefault(Isbn.IsValidIsbn10);

        return new CatalogBook(
            CatalogProvider.OpenLibrary,
            workId,
            doc.Title.Trim(),
            string.IsNullOrWhiteSpace(doc.Subtitle) ? null : doc.Subtitle.Trim(),
            doc.AuthorName ?? [],
            Description: null,
            isbn10,
            isbn13,
            doc.NumberOfPagesMedian is > 0 ? doc.NumberOfPagesMedian : null,
            MapLanguage(doc.Language?.FirstOrDefault()),
            doc.FirstPublishYear?.ToString(CultureInfo.InvariantCulture),
            doc.FirstPublishYear,
            doc.Publisher?.FirstOrDefault(),
            doc.CoverI is > 0 ? CoverUrl(doc.CoverI.Value) : null,
            $"https://openlibrary.org/works/{workId}",
            (doc.Subject ?? []).Take(20).ToList());
    }

    private string? CoverUrl(long coverId) =>
        InputSanitizer.SafeExternalUrl($"{options.Value.CoversBaseUrl.TrimEnd('/')}/b/id/{coverId.ToString(CultureInfo.InvariantCulture)}-M.jpg?default=false");

    private static string? MapLanguage(string? code) =>
        code is null ? null : ThreeToTwo.TryGetValue(code, out var two) ? two : code;

    private static string? ReadText(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        { ValueKind: JsonValueKind.Object } e when e.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String => value.GetString(),
        _ => null,
    };

    private static int? ParseAnyYear(string? value)
    {
        var match = value is null ? null : YearRegex().Match(value);
        return match is { Success: true } && int.TryParse(match.Value, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    [GeneratedRegex("^OL[0-9]{1,12}W$")]
    private static partial Regex WorkIdRegex();

    [GeneratedRegex("^/authors/OL[0-9]{1,12}A$")]
    private static partial Regex AuthorKeyRegex();

    [GeneratedRegex(@"\b(1[5-9]|20)[0-9]{2}\b")]
    private static partial Regex YearRegex();

    internal sealed class SearchResponse
    {
        public int? NumFound { get; set; }

        public List<SearchDoc>? Docs { get; set; }
    }

    internal sealed class SearchDoc
    {
        public string? Key { get; set; }

        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("author_name")]
        public List<string>? AuthorName { get; set; }

        public List<string>? Isbn { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("number_of_pages_median")]
        public int? NumberOfPagesMedian { get; set; }

        public List<string>? Language { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        public List<string>? Publisher { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cover_i")]
        public long? CoverI { get; set; }

        public List<string>? Subject { get; set; }
    }

    internal sealed class Work
    {
        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        public JsonElement? Description { get; set; }

        public List<string>? Subjects { get; set; }

        public List<long>? Covers { get; set; }

        public List<WorkAuthor>? Authors { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("first_publish_date")]
        public string? FirstPublishDate { get; set; }
    }

    internal sealed class WorkAuthor
    {
        public KeyRef? Author { get; set; }
    }

    internal sealed class KeyRef
    {
        public string? Key { get; set; }
    }

    internal sealed class AuthorRecord
    {
        public string? Name { get; set; }
    }
}
