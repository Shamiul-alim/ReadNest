using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.DTOs;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Recommendations;

public sealed record RecommendationDto(BookSummaryDto Book, string Reason, double Score);

/// <summary>Authors and genres the reader likes most; used to suggest external catalogue searches.</summary>
public sealed record DiscoveryTopicsDto(IReadOnlyList<string> Authors, IReadOnlyList<string> Genres);

public sealed record RecommendationsDto(IReadOnlyList<RecommendationDto> Items, DiscoveryTopicsDto Topics, int SignalCount);

public sealed class RecommendationService(IReadNestDbContextFactory dbFactory)
{
    public const int MaxCandidates = 400;

    public async Task<RecommendationsDto> GetRecommendationsAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 50);
        await using var db = await dbFactory.CreateAsync(cancellationToken);

        // Only entries carrying a signal are loaded (ids only), never full book records.
        var entries = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Where(ub => ub.Status != ReadingStatus.WantToRead || ub.IsFavorite || ub.Rating != null)
            .Select(ub => new TasteEntry(
                ub.Book.Authors.Select(a => a.AuthorId).ToList(),
                ub.Book.Genres.Select(g => g.GenreId).ToList(),
                ub.Status,
                ub.Rating,
                ub.IsFavorite))
            .ToListAsync(cancellationToken);

        var profile = RecommendationEngine.BuildProfile(entries);
        if (profile.IsEmpty)
        {
            return new RecommendationsDto([], new DiscoveryTopicsDto([], []), entries.Count);
        }

        var likedAuthors = profile.AuthorWeights.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(50).ToList();
        var likedGenres = profile.GenreWeights.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(30).ToList();

        var candidates = await db.Books.AsNoTracking()
            .Where(b => b.Origin == BookOrigin.Catalog)
            .Where(b => !db.UserBooks.Any(ub => ub.UserId == userId && ub.BookId == b.Id))
            .Where(b => b.Authors.Any(a => likedAuthors.Contains(a.AuthorId)) || b.Genres.Any(g => likedGenres.Contains(g.GenreId)))
            .OrderBy(b => b.Id)
            .Take(MaxCandidates)
            .Select(b => new CandidateBook(
                b.Id,
                b.Authors.Select(a => a.AuthorId).ToList(),
                b.Genres.Select(g => g.GenreId).ToList(),
                db.UserBooks.Count(ub => ub.BookId == b.Id && (ub.Rating >= 4 || ub.Status == ReadingStatus.Read))))
            .ToListAsync(cancellationToken);

        var ranked = RecommendationEngine.Rank(profile, candidates, new HashSet<Guid>(), take);
        var bookIds = ranked.Select(r => r.BookId).ToList();
        var books = await db.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id)).Select(BookProjections.Summary).ToDictionaryAsync(b => b.Id, cancellationToken);

        var reasonAuthorIds = ranked.Where(r => r.Reason == ReasonKind.Author).Select(r => r.ReasonId).Concat(likedAuthors.Take(3)).Distinct().ToList();
        var reasonGenreIds = ranked.Where(r => r.Reason == ReasonKind.Genre).Select(r => r.ReasonId).Concat(likedGenres.Take(3)).Distinct().ToList();
        var authorNames = await db.Authors.AsNoTracking().Where(a => reasonAuthorIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var genreNames = await db.Genres.AsNoTracking().Where(g => reasonGenreIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        var items = ranked
            .Where(r => books.ContainsKey(r.BookId))
            .Select(r => new RecommendationDto(books[r.BookId], ExplainReason(r, authorNames, genreNames), r.Score))
            .ToList();

        var topics = new DiscoveryTopicsDto(
            likedAuthors.Take(3).Where(authorNames.ContainsKey).Select(id => authorNames[id]).ToList(),
            likedGenres.Take(3).Where(genreNames.ContainsKey).Select(id => genreNames[id]).ToList());

        return new RecommendationsDto(items, topics, entries.Count);
    }

    internal static string ExplainReason(ScoredCandidate candidate, IReadOnlyDictionary<Guid, string> authors, IReadOnlyDictionary<Guid, string> genres) =>
        candidate.Reason switch
        {
            ReasonKind.Author when authors.TryGetValue(candidate.ReasonId, out var author) => $"Recommended because you enjoy books by {author}.",
            ReasonKind.Genre when genres.TryGetValue(candidate.ReasonId, out var genre) => $"Recommended because you enjoy books in {genre}.",
            _ => "Recommended because it matches books you enjoyed.",
        };
}
