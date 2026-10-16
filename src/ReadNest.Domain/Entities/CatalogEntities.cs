using ReadNest.Domain.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Domain.Entities;

public sealed class Author : Entity
{
    public const int NameMaxLength = 200;

    private Author()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public List<BookAuthor> Books { get; private set; } = [];

    public static Author Create(string name)
    {
        var clean = TextNormalizer.CollapseWhitespace(Guard.Required(name, NameMaxLength, "author", "Author name"));
        return new Author { Name = clean, NormalizedName = TextNormalizer.Key(clean) };
    }
}

public sealed class Genre : Entity
{
    public const int NameMaxLength = 100;

    private Genre()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public List<BookGenre> Books { get; private set; } = [];

    public static Genre Create(string name, Guid? id = null)
    {
        var clean = TextNormalizer.CollapseWhitespace(Guard.Required(name, NameMaxLength, "genre", "Genre"));
        var genre = new Genre { Name = clean, NormalizedName = TextNormalizer.Key(clean) };
        if (id is { } fixedId)
        {
            genre.Id = fixedId;
        }

        return genre;
    }
}

public sealed class BookAuthor
{
    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public Author Author { get; set; } = null!;

    /// <summary>Order in which the author is credited.</summary>
    public int Position { get; set; }
}

public sealed class BookGenre
{
    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid GenreId { get; set; }

    public Genre Genre { get; set; } = null!;
}

/// <summary>An identifier of a book in an external catalogue. Unique per provider.</summary>
public sealed class BookExternalId
{
    public const int ExternalIdMaxLength = 100;

    public CatalogProvider Provider { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;
}
