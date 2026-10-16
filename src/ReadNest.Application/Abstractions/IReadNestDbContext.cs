using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ReadNest.Domain.Entities;

namespace ReadNest.Application.Abstractions;

/// <summary>
/// Unit of work over the ReadNest database. Application services obtain a short-lived instance per
/// operation from <see cref="IReadNestDbContextFactory"/>; this is required for Blazor Server, where a
/// circuit-scoped context would be shared by concurrent UI events.
/// </summary>
public interface IReadNestDbContext : IAsyncDisposable
{
    DbSet<Book> Books { get; }

    DbSet<Author> Authors { get; }

    DbSet<Genre> Genres { get; }

    DbSet<BookAuthor> BookAuthors { get; }

    DbSet<BookGenre> BookGenres { get; }

    DbSet<BookExternalId> BookExternalIds { get; }

    DbSet<UserBook> UserBooks { get; }

    DbSet<ReadingSession> ReadingSessions { get; }

    DbSet<ReadingNote> ReadingNotes { get; }

    DbSet<ReadingGoal> ReadingGoals { get; }

    DbSet<BookLoan> BookLoans { get; }

    DbSet<CustomShelf> CustomShelves { get; }

    DbSet<CustomShelfBook> CustomShelfBooks { get; }

    DbSet<UserProfile> UserProfiles { get; }

    DbSet<UserFollow> UserFollows { get; }

    DbSet<ReadingChallenge> ReadingChallenges { get; }

    DbSet<ChallengeParticipant> ChallengeParticipants { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the exception was caused by a unique index/constraint violation.</summary>
    bool IsUniqueViolation(DbUpdateException exception);
}

public interface IReadNestDbContextFactory
{
    Task<IReadNestDbContext> CreateAsync(CancellationToken cancellationToken = default);
}
