using ReadNest.Domain.Enums;

namespace ReadNest.Application.Books;

public enum CatalogSearchField
{
    Keyword = 0,
    Title = 1,
    Author = 2,
    Isbn = 3,
    Subject = 4,
}

public sealed record CatalogSearchRequest(
    string Query,
    CatalogSearchField Field = CatalogSearchField.Keyword,
    int Page = 1,
    int PageSize = 20,
    string? Language = null,
    CatalogProvider? Provider = null);

/// <summary>A book record normalised from an external catalogue. Missing values stay null; nothing is invented.</summary>
public sealed record CatalogBook(
    CatalogProvider Provider,
    string ExternalId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? Description,
    string? Isbn10,
    string? Isbn13,
    int? PageCount,
    string? Language,
    string? PublishedDate,
    int? PublishedYear,
    string? Publisher,
    string? CoverUrl,
    string? InfoUrl,
    IReadOnlyList<string> Categories);

public sealed record CatalogProviderPage(IReadOnlyList<CatalogBook> Items, int? TotalItems);

public sealed record CatalogSearchResult(
    IReadOnlyList<CatalogBook> Items,
    int Page,
    int PageSize,
    int? TotalItems,
    CatalogProvider? ServedBy,
    IReadOnlyList<string> Notices)
{
    public bool HasMore => TotalItems is { } total ? Page * PageSize < total : Items.Count >= PageSize;
}

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Order in which providers are tried. Later providers are fallbacks.</summary>
    public List<CatalogProvider> ProviderOrder { get; set; } = [CatalogProvider.GoogleBooks, CatalogProvider.OpenLibrary];

    public int SearchCacheMinutes { get; set; } = 15;

    public int BookCacheMinutes { get; set; } = 120;
}
