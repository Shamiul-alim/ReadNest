using ReadNest.Domain.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Domain.Entities;

/// <summary>
/// A bibliographic record. Catalogue books are shared by every user; manual books are private to their creator.
/// </summary>
public sealed class Book : Entity
{
    public const int TitleMaxLength = 500;
    public const int DescriptionMaxLength = 10_000;
    public const int MaxPageCount = 100_000;

    private Book()
    {
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Upper-cased, accent-free key used for duplicate detection and case-insensitive search.</summary>
    public string NormalizedTitle { get; private set; } = string.Empty;

    public string? Subtitle { get; private set; }

    public string? Description { get; private set; }

    public string? Isbn10 { get; private set; }

    public string? Isbn13 { get; private set; }

    public int? PageCount { get; private set; }

    public string? LanguageCode { get; private set; }

    /// <summary>Publication date exactly as the provider reported it ("2008", "2008-08", "2008-08-01").</summary>
    public string? PublishedDate { get; private set; }

    public int? PublishedYear { get; private set; }

    public string? Publisher { get; private set; }

    public string? CoverImageUrl { get; private set; }

    public string? InfoUrl { get; private set; }

    public BookOrigin Origin { get; private set; }

    /// <summary>Set for manual books only; controls their visibility.</summary>
    public Guid? CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public List<BookAuthor> Authors { get; private set; } = [];

    public List<BookGenre> Genres { get; private set; } = [];

    public List<BookExternalId> ExternalIds { get; private set; } = [];

    public bool IsPrivate => Origin == BookOrigin.Manual;

    public static Book Create(BookMetadata metadata, BookOrigin origin, Guid? createdByUserId, DateTime nowUtc)
    {
        if (origin == BookOrigin.Manual && createdByUserId is null)
        {
            throw new DomainException("A manually entered book must have an owner.");
        }

        var book = new Book
        {
            Origin = origin,
            CreatedByUserId = origin == BookOrigin.Manual ? createdByUserId : null,
            CreatedAtUtc = nowUtc,
        };
        book.ApplyMetadata(metadata, nowUtc);
        return book;
    }

    public void ApplyMetadata(BookMetadata metadata, DateTime nowUtc)
    {
        Title = TextNormalizer.CollapseWhitespace(Guard.Required(metadata.Title, TitleMaxLength, "title", "Title"));
        NormalizedTitle = TextNormalizer.Key(Title);
        Subtitle = Guard.Optional(metadata.Subtitle, TitleMaxLength, "subtitle", "Subtitle");
        Description = Truncate(metadata.Description?.Trim(), DescriptionMaxLength);
        Publisher = Truncate(metadata.Publisher?.Trim(), 300);
        PublishedDate = Truncate(metadata.PublishedDate?.Trim(), 32);
        PublishedYear = metadata.PublishedYear is > 0 and < 3000 ? metadata.PublishedYear : null;
        LanguageCode = Truncate(metadata.LanguageCode?.Trim().ToLowerInvariant(), 16);
        CoverImageUrl = metadata.CoverImageUrl is { Length: <= 1000 } cover ? cover : null;
        InfoUrl = metadata.InfoUrl is { Length: <= 1000 } info ? info : null;

        if (metadata.PageCount is { } pages && (pages <= 0 || pages > MaxPageCount))
        {
            throw new DomainException($"Page count must be between 1 and {MaxPageCount}.", "pageCount");
        }

        PageCount = metadata.PageCount;

        var isbn13 = Isbn.NormalizeToIsbn13(metadata.Isbn13) ?? Isbn.NormalizeToIsbn13(metadata.Isbn10);
        var isbn10Clean = Isbn.Clean(metadata.Isbn10);
        Isbn13 = isbn13;
        Isbn10 = Isbn.IsValidIsbn10(isbn10Clean) ? isbn10Clean : Isbn.ToIsbn10(isbn13);
        UpdatedAtUtc = nowUtc;
    }

    public void SetAuthors(IEnumerable<Author> authors)
    {
        Authors.Clear();
        var position = 0;
        foreach (var author in authors.DistinctBy(a => a.Id))
        {
            Authors.Add(new BookAuthor { BookId = Id, Book = this, AuthorId = author.Id, Author = author, Position = position++ });
        }
    }

    public void SetGenres(IEnumerable<Genre> genres)
    {
        Genres.Clear();
        foreach (var genre in genres.DistinctBy(g => g.Id))
        {
            Genres.Add(new BookGenre { BookId = Id, Book = this, GenreId = genre.Id, Genre = genre });
        }
    }

    public void AddExternalId(CatalogProvider provider, string externalId)
    {
        if (ExternalIds.Any(e => e.Provider == provider && e.ExternalId == externalId))
        {
            return;
        }

        ExternalIds.Add(new BookExternalId { BookId = Id, Book = this, Provider = provider, ExternalId = externalId });
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>Bibliographic values used to create or refresh a <see cref="Book"/>.</summary>
public sealed record BookMetadata(
    string Title,
    string? Subtitle = null,
    string? Description = null,
    string? Isbn10 = null,
    string? Isbn13 = null,
    int? PageCount = null,
    string? LanguageCode = null,
    string? PublishedDate = null,
    int? PublishedYear = null,
    string? Publisher = null,
    string? CoverImageUrl = null,
    string? InfoUrl = null);
