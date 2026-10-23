using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReadNest.Application.Abstractions;
using ReadNest.Domain.Entities;
using ReadNest.Infrastructure.Identity;

namespace ReadNest.Infrastructure.Persistence;

public sealed class ReadNestDbContext(DbContextOptions<ReadNestDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IReadNestDbContext
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();

    public DbSet<BookGenre> BookGenres => Set<BookGenre>();

    public DbSet<BookExternalId> BookExternalIds => Set<BookExternalId>();

    public DbSet<UserBook> UserBooks => Set<UserBook>();

    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();

    public DbSet<ReadingNote> ReadingNotes => Set<ReadingNote>();

    public DbSet<ReadingGoal> ReadingGoals => Set<ReadingGoal>();

    public DbSet<BookLoan> BookLoans => Set<BookLoan>();

    public DbSet<CustomShelf> CustomShelves => Set<CustomShelf>();

    public DbSet<CustomShelfBook> CustomShelfBooks => Set<CustomShelfBook>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserFollow> UserFollows => Set<UserFollow>();

    public DbSet<ReadingChallenge> ReadingChallenges => Set<ReadingChallenge>();

    public DbSet<ChallengeParticipant> ChallengeParticipants => Set<ChallengeParticipant>();

    public DbSet<RecommendationDismissal> RecommendationDismissals => Set<RecommendationDismissal>();

    public DbSet<ReadingFile> ReadingFiles => Set<ReadingFile>();

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ReadNestDbContext).Assembly);

        // Domain entities create their own time-ordered ids. Marking them as never generated lets EF treat an
        // entity added through a navigation (e.g. a loan added to a book's Loans) as new instead of existing.
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(Domain.Common.Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Domain.Common.Entity.Id)).ValueGeneratedNever();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings so the data survives enum reordering and is easy to inspect.
        configurationBuilder.Properties<Domain.Enums.ReadingStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.NoteType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.OwnershipStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.BookFormat>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.ReviewVisibility>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.CatalogProvider>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.BookOrigin>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.ReadingFileFormat>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<Domain.Enums.ReadingFileSource>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Domain.Enums.ReaderTheme>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<Domain.Enums.ReaderWidth>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<Domain.Enums.ReaderFlow>().HaveConversion<string>().HaveMaxLength(16);
    }
}
