using ReadNest.Domain.Enums;

namespace ReadNest.Application.DTOs;

public sealed record BookSummaryDto(
    Guid Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? CoverUrl,
    int? PublishedYear,
    int? PageCount);

public sealed record ExternalLinkDto(CatalogProvider Provider, string ExternalId, string Url);

public sealed record BookDetailsDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Description,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    string? Isbn10,
    string? Isbn13,
    int? PageCount,
    string? LanguageCode,
    string? PublishedDate,
    string? Publisher,
    string? CoverUrl,
    string? InfoUrl,
    BookOrigin Origin,
    IReadOnlyList<ExternalLinkDto> ExternalLinks,
    int PublicReviewCount);

public sealed record NamedCountDto(string Name, int Count);
