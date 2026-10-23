using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;

namespace ReadNest.Infrastructure.ExternalBooks;

/// <summary>Configuration section "ReadingSources".</summary>
public sealed class ReadingSourceOptions
{
    public const string SectionName = "ReadingSources";

    public bool GoogleBooksEnabled { get; set; } = true;

    public bool OpenLibraryEnabled { get; set; } = true;

    public bool GutenbergEnabled { get; set; } = true;

    /// <summary>Gutendex (https://gutendex.com) is an unofficial JSON index of the Project Gutenberg catalogue.</summary>
    public string GutendexBaseUrl { get; set; } = "https://gutendex.com/";
}

/// <summary>
/// Reading availability from official sources: Google Books <c>accessInfo</c>, Open Library search fields
/// (<c>ebook_access</c>) and the Project Gutenberg catalogue via Gutendex. EPUB downloads are only followed on
/// https://www.gutenberg.org (and gutenberg.org), redirects included.
/// </summary>
public sealed partial class ReadingSourceProvider(IHttpClientFactory httpClientFactory, IOptions<ReadingSourceOptions> options, IOptions<GoogleBooksOptions> google)
    : IReadingSourceProvider
{
    internal const string GutendexClient = "Gutendex";
    internal const string GoogleViewApiClient = "GoogleBooksViewApi";
    internal const string GutenbergDownloadClient = "GutenbergDownload";

    private static readonly HashSet<string> GutenbergHosts = new(StringComparer.OrdinalIgnoreCase) { "www.gutenberg.org", "gutenberg.org" };

    public async Task<GoogleAccessInfo?> GetGoogleAccessAsync(string? volumeId, string? isbn, CancellationToken cancellationToken)
    {
        if (!options.Value.GoogleBooksEnabled || !google.Value.Enabled)
        {
            return null;
        }

        var validVolume = volumeId is not null && VolumeIdRegex().IsMatch(volumeId);
        var validIsbn = isbn is not null && IsbnRegex().IsMatch(isbn);
        if (!validVolume && !validIsbn)
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(nameof(GoogleBooksProvider));
        var key = string.IsNullOrWhiteSpace(google.Value.ApiKey) ? string.Empty : "&key=" + Uri.EscapeDataString(google.Value.ApiKey);
        Volume? volume;
        try
        {
            if (validVolume)
            {
                volume = await GetAsync<Volume>(client, $"volumes/{Uri.EscapeDataString(volumeId!)}?fields=id,accessInfo{key}", "Google Books", cancellationToken);
            }
            else
            {
                var list = await GetAsync<VolumeList>(client, $"volumes?q=isbn:{isbn}&maxResults=1&fields=items(id,accessInfo){key}", "Google Books", cancellationToken);
                volume = list?.Items?.FirstOrDefault();
            }
        }
        catch (ReadingSourceUnavailableException)
        {
            // The Books API is quota-limited (429 without an API key). Google's viewability endpoint, which its own
            // embedded viewer uses, answers the same question.
            return await GetGoogleViewabilityAsync(validVolume ? volumeId! : "ISBN:" + isbn, cancellationToken);
        }

        var access = volume?.AccessInfo;
        if (volume?.Id is null || access is null)
        {
            return null;
        }

        var link = access.WebReaderLink is { } url && IsHttpsOn(url, "play.google.com", "books.google.com", "www.google.com") ? url : null;
        return new GoogleAccessInfo(volume.Id, access.Viewability ?? "UNKNOWN", access.Embeddable, access.PublicDomain, link);
    }

    /// <summary>Google Books "Dynamic Links" viewability (https://developers.google.com/books/docs/dynamic-links).</summary>
    private async Task<GoogleAccessInfo?> GetGoogleViewabilityAsync(string bibKey, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(GoogleViewApiClient);
        string body;
        try
        {
            using var response = await client.GetAsync("books?jscmd=viewapi&bibkeys=" + Uri.EscapeDataString(bibKey) + "&callback=cb", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ReadingSourceUnavailableException($"Google Books returned HTTP {(int)response.StatusCode}.");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new ReadingSourceUnavailableException("Google Books could not be reached.", ex);
        }

        return ParseViewability(body, bibKey);
    }

    internal static GoogleAccessInfo? ParseViewability(string body, string bibKey)
    {
        var start = body.IndexOf('(', StringComparison.Ordinal);
        var end = body.LastIndexOf(')');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, ViewApiEntry>>(body[(start + 1)..end], ProviderSupport.Json);
            if (all is null || !all.TryGetValue(bibKey, out var entry) || entry.PreviewUrl is null)
            {
                return null;
            }

            var id = ViewApiIdRegex().Match(entry.PreviewUrl);
            if (!id.Success || !IsHttpsOn(entry.PreviewUrl, "books.google.com"))
            {
                return null;
            }

            var viewability = entry.Preview switch
            {
                "full" => "ALL_PAGES",
                "partial" => "PARTIAL",
                "noview" => "NO_PAGES",
                _ => "UNKNOWN",
            };
            return new GoogleAccessInfo(id.Groups[1].Value, viewability, entry.Embeddable, viewability == "ALL_PAGES", entry.PreviewUrl);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<OpenLibraryAccess?> FindOpenLibraryAsync(string? workId, string? isbn, string title, string? author, CancellationToken cancellationToken)
    {
        if (!options.Value.OpenLibraryEnabled)
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(nameof(OpenLibraryProvider));
        const string fields = "&fields=key,title,ebook_access&limit=1";
        string query;
        if (workId is not null && WorkIdRegex().IsMatch(workId))
        {
            query = "q=" + Uri.EscapeDataString("key:/works/" + workId);
        }
        else if (isbn is not null && IsbnRegex().IsMatch(isbn))
        {
            query = "isbn=" + isbn;
        }
        else
        {
            query = "title=" + Uri.EscapeDataString(title) + (author is null ? string.Empty : "&author=" + Uri.EscapeDataString(author));
        }

        var result = await GetAsync<OpenLibrarySearch>(client, "search.json?" + query + fields, "Open Library", cancellationToken);
        var doc = result?.Docs?.FirstOrDefault();
        var id = doc?.Key?.Split('/').LastOrDefault();
        return id is null || !WorkIdRegex().IsMatch(id) || doc!.EbookAccess is null
            ? null
            : new OpenLibraryAccess(id, doc.Title ?? title, doc.EbookAccess);
    }

    public async Task<IReadOnlyList<GutenbergBook>> SearchGutenbergAsync(string title, string? author, CancellationToken cancellationToken)
    {
        if (!options.Value.GutenbergEnabled)
        {
            return [];
        }

        var terms = (title.Split([':', ';', '('], 2)[0] + " " + author?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()).Trim();
        var client = httpClientFactory.CreateClient(GutendexClient);
        var result = await GetAsync<GutendexPage>(client, "books/?search=" + Uri.EscapeDataString(terms), "Project Gutenberg", cancellationToken);
        return (result?.Results ?? []).Select(Map).OfType<GutenbergBook>().Take(20).ToList();
    }

    public async Task<GutenbergBook?> GetGutenbergAsync(int id, CancellationToken cancellationToken)
    {
        if (!options.Value.GutenbergEnabled || id <= 0)
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(GutendexClient);
        var book = await GetAsync<GutendexBook>(client, "books/" + id.ToString(CultureInfo.InvariantCulture), "Project Gutenberg", cancellationToken, notFoundAsNull: true);
        return book is null ? null : Map(book);
    }

    public async Task<Stream> OpenGutenbergEpubAsync(GutenbergBook book, CancellationToken cancellationToken)
    {
        if (book.EpubUrl is null || !IsGutenbergUrl(book.EpubUrl))
        {
            throw new ReadingSourceUnavailableException("The EPUB link is not on gutenberg.org.");
        }

        var client = httpClientFactory.CreateClient(GutenbergDownloadClient);
        var url = new Uri(book.EpubUrl);
        for (var hop = 0; hop < 4; hop++)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new ReadingSourceUnavailableException("Project Gutenberg download failed.", ex);
            }

            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location is { } location ? new Uri(url, location) : null;
                response.Dispose();
                if (next is null || !IsGutenbergUrl(next.AbsoluteUri))
                {
                    throw new ReadingSourceUnavailableException("Project Gutenberg redirected to an unexpected address.");
                }

                url = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new ReadingSourceUnavailableException($"Project Gutenberg returned HTTP {(int)response.StatusCode}.");
            }

            return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken));
        }

        throw new ReadingSourceUnavailableException("Too many redirects from Project Gutenberg.");
    }

    internal static bool IsGutenbergUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && GutenbergHosts.Contains(uri.Host) && uri.IsDefaultPort;

    internal static GutenbergBook? Map(GutendexBook book)
    {
        if (book.Id <= 0 || string.IsNullOrWhiteSpace(book.Title) || book.MediaType is not (null or "Text"))
        {
            return null;
        }

        var epub = book.Formats is not null
            && book.Formats.TryGetValue("application/epub+zip", out var link)
            && IsGutenbergUrl(link)
                ? link
                : null;
        return new GutenbergBook(
            book.Id,
            book.Title.Trim(),
            (book.Authors ?? []).Select(a => a.Name).OfType<string>().ToList(),
            book.Copyright,
            epub,
            book.Languages ?? []);
    }

    private static bool IsHttpsOn(string url, params string[] hosts) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    private static async Task<T?> GetAsync<T>(HttpClient client, string url, string source, CancellationToken cancellationToken, bool notFoundAsNull = false)
        where T : class
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (notFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ReadingSourceUnavailableException($"{source} returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, ProviderSupport.Json, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or JsonException
            or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new ReadingSourceUnavailableException($"{source} could not be reached.", ex);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{6,20}$")]
    private static partial Regex VolumeIdRegex();

    [GeneratedRegex("[?&]id=([A-Za-z0-9_-]{6,20})")]
    private static partial Regex ViewApiIdRegex();

    [GeneratedRegex("^OL[0-9]{1,10}W$")]
    private static partial Regex WorkIdRegex();

    [GeneratedRegex("^[0-9]{9}[0-9X]$|^[0-9]{13}$")]
    private static partial Regex IsbnRegex();

    /// <summary>Disposes the HTTP response together with its content stream.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class Volume
    {
        public string? Id { get; set; }

        public AccessInfo? AccessInfo { get; set; }
    }

    internal sealed class VolumeList
    {
        public List<Volume>? Items { get; set; }
    }

    internal sealed class AccessInfo
    {
        public string? Viewability { get; set; }

        public bool Embeddable { get; set; }

        public bool PublicDomain { get; set; }

        public string? WebReaderLink { get; set; }
    }

    internal sealed class ViewApiEntry
    {
        public string? Preview { get; set; }

        public bool Embeddable { get; set; }

        [JsonPropertyName("preview_url")]
        public string? PreviewUrl { get; set; }
    }

    internal sealed class OpenLibrarySearch
    {
        public List<OpenLibraryDoc>? Docs { get; set; }
    }

    internal sealed class OpenLibraryDoc
    {
        public string? Key { get; set; }

        public string? Title { get; set; }

        [JsonPropertyName("ebook_access")]
        public string? EbookAccess { get; set; }
    }

    internal sealed class GutendexPage
    {
        public List<GutendexBook>? Results { get; set; }
    }

    internal sealed class GutendexBook
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public List<GutendexPerson>? Authors { get; set; }

        public bool? Copyright { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        public List<string>? Languages { get; set; }

        public Dictionary<string, string>? Formats { get; set; }
    }

    internal sealed class GutendexPerson
    {
        public string? Name { get; set; }
    }
}
