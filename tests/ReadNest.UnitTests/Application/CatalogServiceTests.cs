using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Domain.Enums;

namespace ReadNest.UnitTests.Application;

public sealed class CatalogServiceTests
{
    private static CatalogBook Book(CatalogProvider provider, string id, string title = "Clean Code", string? isbn13 = null) =>
        new(provider, id, title, null, ["Robert C. Martin"], null, null, isbn13, 464, "en", "2008", 2008, null, null, null, []);

    private static IBookCatalogProvider Provider(CatalogProvider kind)
    {
        var provider = Substitute.For<IBookCatalogProvider>();
        provider.Provider.Returns(kind);
        provider.IsEnabled.Returns(true);
        return provider;
    }

    private static CatalogService Service(params IBookCatalogProvider[] providers) =>
        new(providers, new MemoryCache(new MemoryCacheOptions()), Options.Create(new CatalogOptions()), NullLogger<CatalogService>.Instance);

    [Fact]
    public async Task Uses_the_primary_provider_when_it_works()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        var openLibrary = Provider(CatalogProvider.OpenLibrary);
        google.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogProviderPage([Book(CatalogProvider.GoogleBooks, "g1")], 1));

        var result = await Service(google, openLibrary).SearchAsync(new CatalogSearchRequest("clean code"), CancellationToken.None);

        Assert.Equal(CatalogProvider.GoogleBooks, result.ServedBy);
        Assert.Single(result.Items);
        await openLibrary.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task Falls_back_when_the_primary_provider_fails_and_explains_why()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        var openLibrary = Provider(CatalogProvider.OpenLibrary);
        google.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CatalogProviderException(CatalogProvider.GoogleBooks, "boom"));
        openLibrary.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogProviderPage([Book(CatalogProvider.OpenLibrary, "OL1W")], 1));

        var result = await Service(google, openLibrary).SearchAsync(new CatalogSearchRequest("clean code"), CancellationToken.None);

        Assert.Equal(CatalogProvider.OpenLibrary, result.ServedBy);
        Assert.Contains(result.Notices, n => n.Contains("Google Books is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rate_limited_provider_is_skipped_on_the_next_search()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        var openLibrary = Provider(CatalogProvider.OpenLibrary);
        google.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CatalogProviderException(CatalogProvider.GoogleBooks, "429", isRateLimited: true));
        openLibrary.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogProviderPage([Book(CatalogProvider.OpenLibrary, "OL1W")], 1));
        var service = Service(google, openLibrary);

        await service.SearchAsync(new CatalogSearchRequest("first"), CancellationToken.None);
        await service.SearchAsync(new CatalogSearchRequest("second"), CancellationToken.None);

        await google.Received(1).SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task All_providers_failing_raises_a_service_unavailable_error()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        google.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CatalogProviderException(CatalogProvider.GoogleBooks, "down"));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            Service(google).SearchAsync(new CatalogSearchRequest("clean code"), CancellationToken.None));
    }

    [Fact]
    public async Task Results_are_cached()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        google.SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogProviderPage([Book(CatalogProvider.GoogleBooks, "g1")], 1));
        var service = Service(google);

        await service.SearchAsync(new CatalogSearchRequest("Clean Code"), CancellationToken.None);
        await service.SearchAsync(new CatalogSearchRequest("clean code"), CancellationToken.None);

        await google.Received(1).SearchAsync(Arg.Any<CatalogSearchRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_isbn_search_is_a_validation_error_and_valid_one_is_normalised()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        CatalogSearchRequest? seen = null;
        google.SearchAsync(Arg.Do<CatalogSearchRequest>(r => seen = r), Arg.Any<CancellationToken>())
            .Returns(new CatalogProviderPage([], 0));
        var service = Service(google);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.SearchAsync(new CatalogSearchRequest("12345", CatalogSearchField.Isbn), CancellationToken.None));
        await service.SearchAsync(new CatalogSearchRequest("0-13-235088-2", CatalogSearchField.Isbn), CancellationToken.None);

        Assert.Equal("9780132350884", seen?.Query);
    }

    [Fact]
    public async Task Empty_and_oversized_queries_are_rejected()
    {
        var service = Service(Provider(CatalogProvider.GoogleBooks));

        await Assert.ThrowsAsync<RequestValidationException>(() => service.SearchAsync(new CatalogSearchRequest("  "), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.SearchAsync(new CatalogSearchRequest(new string('x', 201)), CancellationToken.None));
    }

    [Fact]
    public void Deduplicates_editions_within_a_page()
    {
        var items = new[]
        {
            Book(CatalogProvider.GoogleBooks, "a", isbn13: "9780132350884"),
            Book(CatalogProvider.GoogleBooks, "b", isbn13: "9780132350884"),
            Book(CatalogProvider.GoogleBooks, "c", title: "Clean  code!"),
            Book(CatalogProvider.GoogleBooks, "d", title: "clean code"),
        };

        var result = CatalogService.Deduplicate(items);

        Assert.Equal(["a", "c"], result.Select(b => b.ExternalId));
    }

    [Fact]
    public async Task Missing_book_is_not_found_rather_than_invented()
    {
        var google = Provider(CatalogProvider.GoogleBooks);
        google.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((CatalogBook?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Service(google).GetBookAsync(CatalogProvider.GoogleBooks, "missing", CancellationToken.None));
    }
}
