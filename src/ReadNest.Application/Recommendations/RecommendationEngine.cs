using ReadNest.Domain.Enums;

namespace ReadNest.Application.Recommendations;

/// <summary>One library entry reduced to the signals the engine needs.</summary>
public sealed record TasteEntry(IReadOnlyList<Guid> AuthorIds, IReadOnlyList<Guid> GenreIds, ReadingStatus Status, int? Rating, bool IsFavorite);

public sealed record CandidateBook(Guid BookId, IReadOnlyList<Guid> AuthorIds, IReadOnlyList<Guid> GenreIds, int Popularity);

public enum ReasonKind
{
    Author = 0,
    Genre = 1,
}

public sealed record ScoredCandidate(Guid BookId, double Score, ReasonKind Reason, Guid ReasonId);

public sealed record TasteProfile(IReadOnlyDictionary<Guid, double> AuthorWeights, IReadOnlyDictionary<Guid, double> GenreWeights)
{
    public bool IsEmpty => !AuthorWeights.Values.Any(w => w > 0) && !GenreWeights.Values.Any(w => w > 0);
}

/// <summary>
/// Transparent, rule-based recommendations (no machine learning). Every library entry contributes a weight to
/// its authors and genres: finished and favourite books and high ratings add weight, low ratings and
/// "did not finish" subtract it. Candidates are scored by the weights of the authors (x2) and genres (x1) they
/// share with the profile, plus a small bonus for books other readers rated highly or finished.
/// </summary>
public static class RecommendationEngine
{
    public const double AuthorMultiplier = 2.0;
    public const double GenreMultiplier = 1.0;
    public const double PopularityMultiplier = 0.25;

    public static double SignalWeight(TasteEntry entry)
    {
        var weight = entry.Status switch
        {
            ReadingStatus.Read => 1.0,
            ReadingStatus.CurrentlyReading => 0.75,
            ReadingStatus.Paused => 0.25,
            ReadingStatus.WantToRead => 0.25,
            ReadingStatus.DidNotFinish => -1.0,
            _ => 0,
        };

        weight += entry.Rating switch
        {
            5 => 2.0,
            4 => 1.0,
            2 => -1.0,
            1 => -2.0,
            _ => 0,
        };

        if (entry.IsFavorite)
        {
            weight += 1.5;
        }

        return weight;
    }

    public static TasteProfile BuildProfile(IEnumerable<TasteEntry> entries)
    {
        var authors = new Dictionary<Guid, double>();
        var genres = new Dictionary<Guid, double>();
        foreach (var entry in entries)
        {
            var weight = SignalWeight(entry);
            foreach (var author in entry.AuthorIds.Distinct())
            {
                authors[author] = authors.GetValueOrDefault(author) + weight;
            }

            foreach (var genre in entry.GenreIds.Distinct())
            {
                genres[genre] = genres.GetValueOrDefault(genre) + weight;
            }
        }

        return new TasteProfile(authors, genres);
    }

    public static IReadOnlyList<ScoredCandidate> Rank(TasteProfile profile, IEnumerable<CandidateBook> candidates, IReadOnlySet<Guid> excludedBookIds, int take)
    {
        var results = new List<ScoredCandidate>();
        foreach (var candidate in candidates)
        {
            if (excludedBookIds.Contains(candidate.BookId))
            {
                continue;
            }

            var bestReason = (Kind: ReasonKind.Genre, Id: Guid.Empty, Contribution: 0.0);
            var score = 0.0;
            foreach (var author in candidate.AuthorIds.Distinct())
            {
                var contribution = profile.AuthorWeights.GetValueOrDefault(author) * AuthorMultiplier;
                score += contribution;
                if (contribution > bestReason.Contribution)
                {
                    bestReason = (ReasonKind.Author, author, contribution);
                }
            }

            foreach (var genre in candidate.GenreIds.Distinct())
            {
                var contribution = profile.GenreWeights.GetValueOrDefault(genre) * GenreMultiplier;
                score += contribution;
                if (contribution > bestReason.Contribution)
                {
                    bestReason = (ReasonKind.Genre, genre, contribution);
                }
            }

            if (bestReason.Contribution <= 0 || score <= 0)
            {
                continue;
            }

            score += Math.Log(1 + Math.Max(0, candidate.Popularity)) * PopularityMultiplier;
            results.Add(new ScoredCandidate(candidate.BookId, Math.Round(score, 4), bestReason.Kind, bestReason.Id));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.BookId)
            .Take(Math.Max(0, take))
            .ToList();
    }
}
