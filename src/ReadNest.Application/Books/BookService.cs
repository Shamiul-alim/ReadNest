using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Application.DTOs;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Books;

/// <summary>Read access to locally stored books. Manual books are only visible to the user who created them.</summary>
public sealed class BookService(IReadNestDbContextFactory dbFactory, CatalogService catalog, BookCatalogImporter importer)
{
    public async Task<BookDetailsDto> GetBookAsync(Guid bookId, Guid? viewerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var book = await VisibleBooks(db, viewerId)
            .AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Subtitle,
                b.Description,
                Authors = b.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                Genres = b.Genres.Select(g => g.Genre.Name).OrderBy(n => n).ToList(),
                b.Isbn10,
                b.Isbn13,
                b.PageCount,
                b.LanguageCode,
                b.PublishedDate,
                b.Publisher,
                b.CoverImageUrl,
                b.InfoUrl,
                b.Origin,
                ExternalIds = b.ExternalIds.Select(e => new { e.Provider, e.ExternalId }).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That book could not be found.");

        var publicReviews = await db.UserBooks.CountAsync(
            ub => ub.BookId == bookId && ub.ReviewVisibility == ReviewVisibility.Public && ub.Review != null,
            cancellationToken);

        return new BookDetailsDto(
            book.Id,
            book.Title,
            book.Subtitle,
            book.Description,
            book.Authors,
            book.Genres,
            book.Isbn10,
            book.Isbn13,
            book.PageCount,
            book.LanguageCode,
            book.PublishedDate,
            book.Publisher,
            book.CoverImageUrl,
            book.InfoUrl,
            book.Origin,
            book.ExternalIds.Select(e => new ExternalLinkDto(e.Provider, e.ExternalId, ExternalUrl(e.Provider, e.ExternalId))).ToList(),
            publicReviews);
    }

    /// <summary>Finds the local copy of an external catalogue record, if it was imported before.</summary>
    public async Task<Guid?> FindByExternalIdAsync(CatalogProvider provider, string externalId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.BookExternalIds
            .Where(e => e.Provider == provider && e.ExternalId == externalId)
            .Select(e => (Guid?)e.BookId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Imports a catalogue book (fetching it from the provider by id) and returns the local book id.</summary>
    public async Task<Guid> ImportFromCatalogAsync(CatalogProvider provider, string externalId, CancellationToken cancellationToken)
    {
        if (await FindByExternalIdAsync(provider, externalId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var source = await catalog.GetBookAsync(provider, externalId, cancellationToken);
        return await ImportAsync(source, cancellationToken);
    }

    public async Task<Guid> ImportAsync(CatalogBook source, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await importer.ImportAsync(db, source, cancellationToken);
    }

    /// <summary>Books sharing an author or genre with the given book, author matches first.</summary>
    public async Task<IReadOnlyList<BookSummaryDto>> GetRelatedBooksAsync(Guid bookId, Guid? viewerId, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 24);
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var authorIds = await db.BookAuthors.Where(a => a.BookId == bookId).Select(a => a.AuthorId).ToListAsync(cancellationToken);
        var genreIds = await db.BookGenres.Where(g => g.BookId == bookId).Select(g => g.GenreId).ToListAsync(cancellationToken);
        if (authorIds.Count == 0 && genreIds.Count == 0)
        {
            return [];
        }

        return await VisibleBooks(db, viewerId)
            .AsNoTracking()
            .Where(b => b.Id != bookId && b.Origin == BookOrigin.Catalog)
            .Where(b => b.Authors.Any(a => authorIds.Contains(a.AuthorId)) || b.Genres.Any(g => genreIds.Contains(g.GenreId)))
            .OrderByDescending(b => b.Authors.Count(a => authorIds.Contains(a.AuthorId)))
            .ThenByDescending(b => b.Genres.Count(g => genreIds.Contains(g.GenreId)))
            .ThenBy(b => b.Title)
            .Take(take)
            .Select(BookProjections.Summary)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetGenreNamesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.Genres.AsNoTracking().OrderBy(g => g.Name).Select(g => g.Name).Take(500).ToListAsync(cancellationToken);
    }

    public static string ExternalUrl(CatalogProvider provider, string externalId) => provider switch
    {
        CatalogProvider.GoogleBooks => $"https://books.google.com/books?id={Uri.EscapeDataString(externalId)}",
        CatalogProvider.OpenLibrary => $"https://openlibrary.org/works/{Uri.EscapeDataString(externalId)}",
        _ => string.Empty,
    };

    internal static IQueryable<Book> VisibleBooks(IReadNestDbContext db, Guid? viewerId) =>
        db.Books.Where(b => b.Origin == BookOrigin.Catalog || (viewerId != null && b.CreatedByUserId == viewerId));
}

public static class BookProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<Book, BookSummaryDto>> Summary = b => new BookSummaryDto(
        b.Id,
        b.Title,
        b.Subtitle,
        b.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
        b.CoverImageUrl,
        b.PublishedYear,
        b.PageCount);
}
