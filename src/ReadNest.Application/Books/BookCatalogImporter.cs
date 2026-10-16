using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Books;

public sealed record ManualBookInput(
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? Isbn,
    int? PageCount,
    int? PublishedYear,
    string? Publisher,
    string? LanguageCode,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<string> Genres);

/// <summary>
/// Saves catalogue records as local books without creating duplicates.
/// Matching order: (1) provider + external id, (2) normalised ISBN-13, (3) for records without any ISBN,
/// identical normalised title and first author. Manual books stay private to their creator.
/// </summary>
public sealed class BookCatalogImporter(UserClock clock)
{
    public async Task<Guid> ImportAsync(IReadNestDbContext db, CatalogBook source, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ImportOnceAsync(db, source, cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt == 0 && db.IsUniqueViolation(ex))
            {
                // A concurrent request inserted the same book/author/genre first; retry to pick it up.
                DetachAll(db);
            }
        }
    }

    public async Task<Guid> CreateManualAsync(IReadNestDbContext db, Guid userId, ManualBookInput input, CancellationToken cancellationToken)
    {
        var book = await ResolveManualAsync(db, userId, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return book.Id;
    }

    /// <summary>
    /// Returns an existing book matching the manual input (a shared catalogue book with the same ISBN, or one of
    /// the user's own manual books), or a new private book that has been added to the context but not saved.
    /// </summary>
    public async Task<Book> ResolveManualAsync(IReadNestDbContext db, Guid userId, ManualBookInput input, CancellationToken cancellationToken)
    {
        string? isbn13 = null;
        if (!string.IsNullOrWhiteSpace(input.Isbn))
        {
            isbn13 = Isbn.NormalizeToIsbn13(input.Isbn)
                ?? throw new RequestValidationException("isbn", "Enter a valid ISBN-10 or ISBN-13, or leave it empty.");
        }

        var coverUrl = InputSanitizer.RequireSafeUrlOrNull(input.CoverUrl, "coverUrl");
        var authors = CategoryCleaner.CleanAuthors(input.Authors);
        var title = input.Title?.Trim() ?? string.Empty;

        if (isbn13 is not null)
        {
            // Reuse a shared catalogue record or the user's own manual record with the same ISBN.
            var existing = await db.Books
                .Where(b => b.Isbn13 == isbn13 && (b.Origin == BookOrigin.Catalog || b.CreatedByUserId == userId))
                .OrderBy(b => b.Origin)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }
        else if (title.Length > 0)
        {
            var titleKey = TextNormalizer.Key(title);
            var authorKey = authors.Count > 0 ? TextNormalizer.Key(authors[0]) : null;
            var existing = await db.Books
                .Where(b => b.CreatedByUserId == userId && b.NormalizedTitle == titleKey)
                .Where(b => authorKey == null
                    ? !b.Authors.Any()
                    : b.Authors.Any(a => a.Position == 0 && a.Author.NormalizedName == authorKey))
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var now = clock.UtcNow;
        var book = Book.Create(
            new BookMetadata(
                Title: title,
                Subtitle: input.Subtitle,
                Description: input.Description,
                Isbn13: isbn13,
                PageCount: input.PageCount,
                LanguageCode: input.LanguageCode,
                PublishedYear: input.PublishedYear,
                PublishedDate: input.PublishedYear?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Publisher: input.Publisher,
                CoverImageUrl: coverUrl),
            BookOrigin.Manual,
            userId,
            now);

        book.SetAuthors(await ResolveAuthorsAsync(db, authors, cancellationToken));
        book.SetGenres(await ResolveGenresAsync(db, CategoryCleaner.Clean(input.Genres), cancellationToken));
        db.Books.Add(book);
        return book;
    }

    private async Task<Guid> ImportOnceAsync(IReadNestDbContext db, CatalogBook source, CancellationToken cancellationToken)
    {
        var byExternalId = await db.BookExternalIds
            .Where(e => e.Provider == source.Provider && e.ExternalId == source.ExternalId)
            .Select(e => (Guid?)e.BookId)
            .FirstOrDefaultAsync(cancellationToken);
        if (byExternalId is { } knownId)
        {
            return knownId;
        }

        var isbn13 = Isbn.NormalizeToIsbn13(source.Isbn13) ?? Isbn.NormalizeToIsbn13(source.Isbn10);
        var authors = CategoryCleaner.CleanAuthors(source.Authors);
        Book? match;
        if (isbn13 is not null)
        {
            match = await db.Books
                .Include(b => b.ExternalIds)
                .FirstOrDefaultAsync(b => b.Origin == BookOrigin.Catalog && b.Isbn13 == isbn13, cancellationToken);
        }
        else
        {
            var titleKey = TextNormalizer.Key(source.Title);
            var authorKey = authors.Count > 0 ? TextNormalizer.Key(authors[0]) : null;
            match = authorKey is null
                ? null
                : await db.Books
                    .Include(b => b.ExternalIds)
                    .Where(b => b.Origin == BookOrigin.Catalog && b.Isbn13 == null && b.NormalizedTitle == titleKey)
                    .Where(b => b.Authors.Any(a => a.Position == 0 && a.Author.NormalizedName == authorKey))
                    .FirstOrDefaultAsync(cancellationToken);
        }

        if (match is not null)
        {
            match.AddExternalId(source.Provider, source.ExternalId);
            await db.SaveChangesAsync(cancellationToken);
            return match.Id;
        }

        var now = clock.UtcNow;
        var book = Book.Create(
            new BookMetadata(
                Title: source.Title,
                Subtitle: source.Subtitle,
                Description: source.Description,
                Isbn10: source.Isbn10,
                Isbn13: isbn13,
                PageCount: source.PageCount is > 0 and <= Book.MaxPageCount ? source.PageCount : null,
                LanguageCode: source.Language,
                PublishedDate: source.PublishedDate,
                PublishedYear: source.PublishedYear,
                Publisher: source.Publisher,
                CoverImageUrl: InputSanitizer.SafeExternalUrl(source.CoverUrl),
                InfoUrl: InputSanitizer.SafeExternalUrl(source.InfoUrl)),
            BookOrigin.Catalog,
            createdByUserId: null,
            now);

        book.SetAuthors(await ResolveAuthorsAsync(db, authors, cancellationToken));
        book.SetGenres(await ResolveGenresAsync(db, CategoryCleaner.Clean(source.Categories), cancellationToken));
        book.AddExternalId(source.Provider, source.ExternalId);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);
        return book.Id;
    }

    internal static async Task<List<Author>> ResolveAuthorsAsync(IReadNestDbContext db, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var keys = names.Select(TextNormalizer.Key).ToList();
        var existing = await db.Authors.Where(a => keys.Contains(a.NormalizedName)).ToListAsync(cancellationToken);
        var result = new List<Author>(names.Count);
        foreach (var name in names)
        {
            var key = TextNormalizer.Key(name);
            var author = existing.FirstOrDefault(a => a.NormalizedName == key);
            if (author is null)
            {
                author = Author.Create(name);
                db.Authors.Add(author);
                existing.Add(author);
            }

            result.Add(author);
        }

        return result;
    }

    internal static async Task<List<Genre>> ResolveGenresAsync(IReadNestDbContext db, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var keys = names.Select(TextNormalizer.Key).ToList();
        var existing = await db.Genres.Where(g => keys.Contains(g.NormalizedName)).ToListAsync(cancellationToken);
        var result = new List<Genre>(names.Count);
        foreach (var name in names)
        {
            var key = TextNormalizer.Key(name);
            var genre = existing.FirstOrDefault(g => g.NormalizedName == key);
            if (genre is null)
            {
                genre = Genre.Create(name);
                db.Genres.Add(genre);
                existing.Add(genre);
            }

            result.Add(genre);
        }

        return result;
    }

    private static void DetachAll(IReadNestDbContext db)
    {
        if (db is DbContext context)
        {
            context.ChangeTracker.Clear();
        }
    }
}
