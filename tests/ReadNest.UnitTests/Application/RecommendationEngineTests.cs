using ReadNest.Application.Recommendations;
using ReadNest.Domain.Enums;

namespace ReadNest.UnitTests.Application;

public sealed class RecommendationEngineTests
{
    private static readonly Guid Martin = Guid.NewGuid();
    private static readonly Guid Fowler = Guid.NewGuid();
    private static readonly Guid Romance = Guid.NewGuid();
    private static readonly Guid SoftwareEngineering = Guid.NewGuid();
    private static readonly Guid Horror = Guid.NewGuid();

    [Fact]
    public void Signal_weights_reward_finished_favourite_and_high_ratings()
    {
        var loved = new TasteEntry([], [], ReadingStatus.Read, 5, true);
        var abandoned = new TasteEntry([], [], ReadingStatus.DidNotFinish, 1, false);

        Assert.Equal(4.5, RecommendationEngine.SignalWeight(loved));
        Assert.Equal(-3.0, RecommendationEngine.SignalWeight(abandoned));
    }

    [Fact]
    public void Author_matches_outrank_genre_matches()
    {
        var profile = RecommendationEngine.BuildProfile(
        [
            new TasteEntry([Martin], [SoftwareEngineering], ReadingStatus.Read, 5, false),
        ]);
        var byAuthor = new CandidateBook(Guid.NewGuid(), [Martin], [], 0);
        var byGenre = new CandidateBook(Guid.NewGuid(), [Fowler], [SoftwareEngineering], 0);

        var ranked = RecommendationEngine.Rank(profile, [byGenre, byAuthor], new HashSet<Guid>(), 10);

        Assert.Equal(byAuthor.BookId, ranked[0].BookId);
        Assert.Equal(ReasonKind.Author, ranked[0].Reason);
        Assert.Equal(Martin, ranked[0].ReasonId);
        Assert.Equal(ReasonKind.Genre, ranked[1].Reason);
    }

    [Fact]
    public void Disliked_genres_and_unrelated_books_are_not_recommended()
    {
        var profile = RecommendationEngine.BuildProfile(
        [
            new TasteEntry([], [Horror], ReadingStatus.DidNotFinish, 1, false),
            new TasteEntry([], [SoftwareEngineering], ReadingStatus.Read, 4, false),
        ]);
        var horror = new CandidateBook(Guid.NewGuid(), [], [Horror], 50);
        var unrelated = new CandidateBook(Guid.NewGuid(), [], [Romance], 50);
        var relevant = new CandidateBook(Guid.NewGuid(), [], [SoftwareEngineering], 0);

        var ranked = RecommendationEngine.Rank(profile, [horror, unrelated, relevant], new HashSet<Guid>(), 10);

        Assert.Single(ranked);
        Assert.Equal(relevant.BookId, ranked[0].BookId);
    }

    [Fact]
    public void Books_already_in_the_library_are_excluded()
    {
        var profile = RecommendationEngine.BuildProfile([new TasteEntry([Martin], [], ReadingStatus.Read, 5, false)]);
        var owned = new CandidateBook(Guid.NewGuid(), [Martin], [], 0);

        var ranked = RecommendationEngine.Rank(profile, [owned], new HashSet<Guid> { owned.BookId }, 10);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Popularity_breaks_ties_and_take_limits_results()
    {
        var profile = RecommendationEngine.BuildProfile([new TasteEntry([], [SoftwareEngineering], ReadingStatus.Read, null, false)]);
        var quiet = new CandidateBook(Guid.NewGuid(), [], [SoftwareEngineering], 0);
        var popular = new CandidateBook(Guid.NewGuid(), [], [SoftwareEngineering], 20);
        var third = new CandidateBook(Guid.NewGuid(), [], [SoftwareEngineering], 5);

        var ranked = RecommendationEngine.Rank(profile, [quiet, popular, third], new HashSet<Guid>(), 2);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(popular.BookId, ranked[0].BookId);
        Assert.Equal(third.BookId, ranked[1].BookId);
    }

    [Fact]
    public void Empty_profile_is_detected()
    {
        var profile = RecommendationEngine.BuildProfile([new TasteEntry([Martin], [Horror], ReadingStatus.DidNotFinish, null, false)]);

        Assert.True(profile.IsEmpty);
    }

    [Fact]
    public void Reasons_are_explained_in_plain_language()
    {
        var candidate = new ScoredCandidate(Guid.NewGuid(), 3, ReasonKind.Genre, SoftwareEngineering);
        var text = RecommendationService.ExplainReason(candidate, new Dictionary<Guid, string>(), new Dictionary<Guid, string> { [SoftwareEngineering] = "Software Engineering" });

        Assert.Equal("Recommended because you enjoy books in Software Engineering.", text);
    }
}
