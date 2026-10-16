using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Domain.Enums;

namespace ReadNest.IntegrationTests.Infrastructure;

/// <summary>Deterministic stand-in for Google Books so tests never depend on the network.</summary>
public sealed class FakeCatalogProvider : IBookCatalogProvider
{
    public static readonly CatalogBook CleanCode = new(
        CatalogProvider.GoogleBooks, "clean-code", "Clean Code", "A Handbook of Agile Software Craftsmanship", ["Robert C. Martin"],
        "Even bad code can function.", "0132350882", "9780132350884", 464, "en", "2008-08-01", 2008, "Prentice Hall",
        "https://example.test/clean-code.jpg", "https://books.google.com/books?id=clean-code", ["Computers / Software Development"]);

    public static readonly CatalogBook CleanArchitecture = new(
        CatalogProvider.GoogleBooks, "clean-architecture", "Clean Architecture", null, ["Robert C. Martin"],
        null, null, "9780134494166", 432, "en", "2017", 2017, "Prentice Hall", null, null, ["Computers / Software Development"]);

    public static readonly CatalogBook Refactoring = new(
        CatalogProvider.GoogleBooks, "refactoring", "Refactoring", null, ["Martin Fowler"],
        null, null, "9780134757599", 448, "en", "2018", 2018, "Addison-Wesley", null, null, ["Computers / Software Development"]);

    public static readonly CatalogBook NoIsbn = new(
        CatalogProvider.GoogleBooks, "no-isbn", "Pamphlet Without Identifiers", null, ["Anonymous"],
        null, null, null, null, null, null, null, null, null, null, []);

    private readonly List<CatalogBook> _books = [CleanCode, CleanArchitecture, Refactoring, NoIsbn];

    /// <summary>When set, every call fails as if the provider were down.</summary>
    public bool Unavailable { get; set; }

    public CatalogProvider Provider => CatalogProvider.GoogleBooks;

    public bool IsEnabled => true;

    public Task<CatalogProviderPage> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new CatalogProviderException(Provider, "Simulated outage.");
        }

        var matches = _books
            .Where(b => b.Title.Contains(request.Query, StringComparison.OrdinalIgnoreCase)
                        || b.Authors.Any(a => a.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                        || b.Isbn13 == request.Query)
            .ToList();
        return Task.FromResult(new CatalogProviderPage(matches.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList(), matches.Count));
    }

    public Task<CatalogBook?> GetByIdAsync(string externalId, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new CatalogProviderException(Provider, "Simulated outage.");
        }

        return Task.FromResult(_books.FirstOrDefault(b => b.ExternalId == externalId));
    }
}
