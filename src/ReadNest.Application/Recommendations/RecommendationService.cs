using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Application.DTOs;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Recommendations;

/// <summary>A suggestion with the reason it is there. <see cref="Basis"/> says whether an author or a subject drove it.</summary>
public sealed record RecommendationDto(BookSummaryDto Book, string Reason, double Score, ReasonKind Basis);

/// <summary>Authors and genres the reader likes most; used to suggest external catalogue searches.</summary>
public sealed record DiscoveryTopicsDto(IReadOnlyList<string> Authors, IReadOnlyList<string> Genres);

public sealed record RecommendationsDto(IReadOnlyList<RecommendationDto> Items, DiscoveryTopicsDto Topics, int SignalCount, int DismissedCount = 0);

/// <summary>
/// Rule-based suggestions from the reader's own library. Books the reader marked "Not for me" are left out until
/// they restore their dismissed suggestions.
/// </summary>
public sealed class RecommendationService(IReadNestDbContextFactory dbFactory, UserClock clock)
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

        var dismissed = await db.RecommendationDismissals.AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => d.BookId)
            .ToListAsync(cancellationToken);

        var profile = RecommendationEngine.BuildProfile(entries);
        if (profile.IsEmpty)
        {
            return new RecommendationsDto([], new DiscoveryTopicsDto([], []), entries.Count, dismissed.Count);
        }

        var likedAuthors = profile.AuthorWeights.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(50).ToList();
        var likedGenres = profile.GenreWeights.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(30).ToList();

        var candidates = await db.Books.AsNoTracking()
            .Where(b => b.Origin == BookOrigin.Catalog)
            // Also skip other records of a book the reader already has: the same ISBN, or another edition (same title
            // and a shared author), e.g. a manual record imported from a CSV.
            .Where(b => !db.UserBooks.Any(ub => ub.UserId == userId
                && (ub.BookId == b.Id
                    || (b.Isbn13 != null && ub.Book.Isbn13 == b.Isbn13)
                    || (ub.Book.NormalizedTitle == b.NormalizedTitle
                        && ub.Book.Authors.Any(a => b.Authors.Any(x => x.Author.NormalizedName == a.Author.NormalizedName))))))
            .Where(b => !db.RecommendationDismissals.Any(d => d.UserId == userId && d.BookId == b.Id))
            .Where(b => b.Authors.Any(a => likedAuthors.Contains(a.AuthorId)) || b.Genres.Any(g => likedGenres.Contains(g.GenreId)))
            .OrderBy(b => b.Id)
            .Take(MaxCandidates)
            .Select(b => new CandidateBook(
                b.Id,
                b.Authors.Select(a => a.AuthorId).ToList(),
                b.Genres.Select(g => g.GenreId).ToList(),
                db.UserBooks.Count(ub => ub.BookId == b.Id && (ub.Rating >= 4 || ub.Status == ReadingStatus.Read))))
            .ToListAsync(cancellationToken);

        var ranked = RecommendationEngine.Rank(profile, candidates, dismissed.ToHashSet(), take);
        var bookIds = ranked.Select(r => r.BookId).ToList();
        var books = await db.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id)).Select(BookProjections.Summary).ToDictionaryAsync(b => b.Id, cancellationToken);

        var reasonAuthorIds = ranked.Where(r => r.Reason == ReasonKind.Author).Select(r => r.ReasonId).Concat(likedAuthors.Take(3)).Distinct().ToList();
        var reasonGenreIds = ranked.Where(r => r.Reason == ReasonKind.Genre).Select(r => r.ReasonId).Concat(likedGenres.Take(3)).Distinct().ToList();
        var authorNames = await db.Authors.AsNoTracking().Where(a => reasonAuthorIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var genreNames = await db.Genres.AsNoTracking().Where(g => reasonGenreIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);

        var items = ranked
            .Where(r => books.ContainsKey(r.BookId))
            .Select(r => new RecommendationDto(books[r.BookId], ExplainReason(r, authorNames, genreNames), r.Score, r.Reason))
            .ToList();

        var topics = new DiscoveryTopicsDto(
            likedAuthors.Take(3).Where(authorNames.ContainsKey).Select(id => authorNames[id]).ToList(),
            likedGenres.Take(3).Where(genreNames.ContainsKey).Select(id => genreNames[id]).ToList());

        return new RecommendationsDto(items, topics, entries.Count, dismissed.Count);
    }

    /// <summary>Hides a suggestion from this reader. Idempotent.</summary>
    public async Task DismissAsync(Guid userId, Guid bookId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        if (!await db.Books.AnyAsync(b => b.Id == bookId && b.Origin == BookOrigin.Catalog, cancellationToken))
        {
            throw new NotFoundException("That book was not found.");
        }

        if (await db.RecommendationDismissals.AnyAsync(d => d.UserId == userId && d.BookId == bookId, cancellationToken))
        {
            return;
        }

        db.RecommendationDismissals.Add(RecommendationDismissal.Create(userId, bookId, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Dismissed twice at once: the outcome is the same.
        }
    }

    /// <summary>Brings back one dismissed suggestion (used by Undo).</summary>
    public async Task RestoreAsync(Guid userId, Guid bookId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var dismissal = await db.RecommendationDismissals.FirstOrDefaultAsync(d => d.UserId == userId && d.BookId == bookId, cancellationToken);
        if (dismissal is not null)
        {
            db.RecommendationDismissals.Remove(dismissal);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Brings back every dismissed suggestion. Returns how many were restored.</summary>
    public async Task<int> RestoreAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.RecommendationDismissals.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    internal static string ExplainReason(ScoredCandidate candidate, IReadOnlyDictionary<Guid, string> authors, IReadOnlyDictionary<Guid, string> genres) =>
        candidate.Reason switch
        {
            ReasonKind.Author when authors.TryGetValue(candidate.ReasonId, out var author) => $"Recommended because you enjoy books by {author}.",
            ReasonKind.Genre when genres.TryGetValue(candidate.ReasonId, out var genre) => $"Recommended because you enjoy books in {genre}.",
            _ => "Recommended because it matches books you enjoyed.",
        };
}
