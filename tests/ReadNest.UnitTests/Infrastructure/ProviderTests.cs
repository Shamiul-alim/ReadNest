using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Infrastructure.ExternalBooks;

namespace ReadNest.UnitTests.Infrastructure;

public sealed class ProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GoogleBooksProvider Google(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/books/v1/") }, Options.Create(new GoogleBooksOptions()));

    private static OpenLibraryProvider OpenLibrary(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") }, Options.Create(new OpenLibraryOptions()));

    [Fact]
    public async Task Google_maps_volumes_and_handles_missing_fields()
    {
        const string payload = """
        {"totalItems": 2, "items": [
          {"id": "abc123", "volumeInfo": {
             "title": "Clean Code", "subtitle": "A Handbook", "authors": ["Robert C. Martin"],
             "publishedDate": "2008-08-01", "description": "<p>Even <b>bad</b> code&amp;more</p>",
             "industryIdentifiers": [{"type": "ISBN_10", "identifier": "0132350882"}, {"type": "ISBN_13", "identifier": "9780132350884"}],
             "pageCount": 464, "categories": ["Computers / Software Development"],
             "imageLinks": {"thumbnail": "http://books.google.com/books/content?id=abc123&edge=curl"}, "language": "en"}},
          {"id": "noinfo"}
        ]}
        """;
        var handler = new StubHandler(_ => Json(payload));

        var page = await Google(handler).SearchAsync(new CatalogSearchRequest("clean code", CatalogSearchField.Title, 2, 10, "en"), CancellationToken.None);

        var book = Assert.Single(page.Items);
        Assert.Equal("Clean Code", book.Title);
        Assert.Equal("9780132350884", book.Isbn13);
        Assert.Equal(2008, book.PublishedYear);
        Assert.Equal("Even bad code&more", book.Description);
        Assert.Equal("https://books.google.com/books/content?id=abc123", book.CoverUrl);
        var query = handler.Requests[0].Query;
        Assert.Contains("intitle%3A%22clean%20code%22", query, StringComparison.Ordinal);
        Assert.Contains("startIndex=10", query, StringComparison.Ordinal);
        Assert.Contains("langRestrict=en", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Google_rate_limit_is_reported()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<CatalogProviderException>(() => Google(handler).SearchAsync(new CatalogSearchRequest("x"), CancellationToken.None));
        Assert.True(ex.IsRateLimited);
    }

    [Fact]
    public async Task Malformed_payload_becomes_a_provider_error()
    {
        var handler = new StubHandler(_ => Json("{ not json"));

        await Assert.ThrowsAsync<CatalogProviderException>(() => Google(handler).SearchAsync(new CatalogSearchRequest("x"), CancellationToken.None));
    }

    [Fact]
    public async Task Server_errors_become_provider_errors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<CatalogProviderException>(() => OpenLibrary(handler).SearchAsync(new CatalogSearchRequest("x"), CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_or_invalid_ids_return_null()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await Google(handler).GetByIdAsync("doesnotexist", CancellationToken.None));
        Assert.Null(await Google(handler).GetByIdAsync("../../etc/passwd", CancellationToken.None));
        Assert.Null(await OpenLibrary(handler).GetByIdAsync("not-a-work", CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Open_library_search_maps_documents()
    {
        const string payload = """
        {"numFound": 1, "docs": [{
          "key": "/works/OL17618370W", "title": "Clean Code", "author_name": ["Robert C. Martin"],
          "isbn": ["8441540845", "9780136083221", "9780132350884"], "number_of_pages_median": 444,
          "language": ["eng"], "first_publish_year": 2008, "publisher": ["Prentice Hall"], "cover_i": 8065615,
          "subject": ["Agile software development", "Accessible book"]}]}
        """;
        var handler = new StubHandler(_ => Json(payload));

        var page = await OpenLibrary(handler).SearchAsync(new CatalogSearchRequest("9780132350884", CatalogSearchField.Isbn), CancellationToken.None);

        var book = Assert.Single(page.Items);
        Assert.Equal("OL17618370W", book.ExternalId);
        Assert.Equal("9780132350884", book.Isbn13);
        Assert.Equal("en", book.Language);
        Assert.Equal(444, book.PageCount);
        Assert.Equal("https://covers.openlibrary.org/b/id/8065615-M.jpg?default=false", book.CoverUrl);
        Assert.Equal(["Agile software development"], CategoryCleaner.Clean(book.Categories));
    }

    [Theory]
    [InlineData("https://example.com/cover.jpg", "https://example.com/cover.jpg")]
    [InlineData("http://example.com/a.png", "https://example.com/a.png")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/png;base64,AAAA", null)]
    [InlineData("https://user:pass@example.com/x", null)]
    [InlineData("/relative/path.png", null)]
    public void External_urls_are_sanitised(string input, string? expected) =>
        Assert.Equal(expected, InputSanitizer.SafeExternalUrl(input));
}
