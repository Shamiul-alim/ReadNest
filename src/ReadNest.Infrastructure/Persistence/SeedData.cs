using System.Security.Cryptography;
using System.Text;
using ReadNest.Domain.Entities;

namespace ReadNest.Infrastructure.Persistence;

/// <summary>
/// Non-sensitive reference data applied through migrations. Ids are derived from the name so they stay stable
/// across model builds.
/// </summary>
internal static class SeedData
{
    private static readonly string[] GenreNames =
    [
        "Fiction", "Science Fiction", "Fantasy", "Mystery", "Thriller", "Romance", "Historical Fiction", "Horror",
        "Literary Fiction", "Young Adult", "Children's", "Graphic Novels", "Poetry", "Drama", "Biography",
        "Memoir", "History", "Science", "Mathematics", "Philosophy", "Psychology", "Self-Help",
        "Personal Development", "Business", "Economics", "Politics", "Religion", "Art", "Music", "Travel",
        "Cooking", "Health", "Education", "Computers", "Software Engineering", "Programming", "Technology",
        "Design", "Nature", "Sports",
    ];

    public static IReadOnlyList<Genre> Genres { get; } = GenreNames.Select(name => Genre.Create(name, StableId("genre:" + name))).ToList();

    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
