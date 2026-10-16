using ReadNest.Domain.Entities;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Books;

/// <summary>Turns noisy provider categories/subjects into a short list of usable genre names.</summary>
public static class CategoryCleaner
{
    public const int MaxGenresPerBook = 5;

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "General", "Accessible book", "Protected DAISY", "In library", "Lending library", "Large type books",
        "Open Library Staff Picks", "Fiction, general", "Nonfiction", "Non-fiction", "Readers", "Textbooks",
        "Juvenile literature", "Internet Archive Wishlist", "Overdrive", "Long Now Manual for Civilization",
    };

    public static IReadOnlyList<string> Clean(IEnumerable<string> categories)
    {
        var result = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in categories)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains(':', StringComparison.Ordinal) || raw.Contains('=', StringComparison.Ordinal))
            {
                continue;
            }

            // Google Books uses BISAC-like paths such as "Computers / Software Development".
            foreach (var segment in raw.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(2))
            {
                var name = TextNormalizer.CollapseWhitespace(segment).Trim('.', ',', ' ');
                if (name.Length is < 3 or > 40 || Noise.Contains(name) || name.Any(char.IsDigit))
                {
                    continue;
                }

                name = char.ToUpperInvariant(name[0]) + name[1..];
                if (keys.Add(TextNormalizer.Key(name)))
                {
                    result.Add(name);
                }

                if (result.Count == MaxGenresPerBook)
                {
                    return result;
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<string> CleanAuthors(IEnumerable<string> authors) =>
        authors
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => TextNormalizer.CollapseWhitespace(a))
            .Where(a => a.Length <= Author.NameMaxLength)
            .DistinctBy(TextNormalizer.Key)
            .Take(10)
            .ToList();
}
