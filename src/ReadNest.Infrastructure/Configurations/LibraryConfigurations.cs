using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReadNest.Domain.Entities;
using ReadNest.Infrastructure.Identity;

namespace ReadNest.Infrastructure.Configurations;

internal sealed class UserBookConfiguration : IEntityTypeConfiguration<UserBook>
{
    public void Configure(EntityTypeBuilder<UserBook> builder)
    {
        builder.ToTable("UserBooks", t =>
        {
            t.HasCheckConstraint("CK_UserBooks_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_UserBooks_CurrentPage", "\"CurrentPage\" >= 0");
            t.HasCheckConstraint("CK_UserBooks_TotalPages", "\"TotalPages\" IS NULL OR (\"TotalPages\" > 0 AND \"CurrentPage\" <= \"TotalPages\")");
            t.HasCheckConstraint("CK_UserBooks_CompletedRequiresRead", "\"CompletedOn\" IS NULL OR \"Status\" = 'Read'");
            t.HasCheckConstraint("CK_UserBooks_StartBeforeCompletion", "\"StartedOn\" IS NULL OR \"CompletedOn\" IS NULL OR \"StartedOn\" <= \"CompletedOn\"");
            t.HasCheckConstraint("CK_UserBooks_PurchasePrice", "\"PurchasePrice\" IS NULL OR \"PurchasePrice\" >= 0");
            t.HasCheckConstraint("CK_UserBooks_PublicReviewHasText", "\"ReviewVisibility\" = 'Private' OR \"Review\" IS NOT NULL");
        });
        builder.HasKey(ub => ub.Id);
        builder.Property(ub => ub.Review).HasMaxLength(UserBook.ReviewMaxLength);
        builder.Property(ub => ub.PrivateNotes).HasMaxLength(UserBook.PrivateNotesMaxLength);
        builder.Property(ub => ub.ConditionNotes).HasMaxLength(UserBook.ConditionNotesMaxLength);
        builder.Property(ub => ub.PurchasePrice).HasPrecision(12, 2);
        builder.Ignore(ub => ub.ProgressPercent);

        // One library entry per user and book.
        builder.HasIndex(ub => new { ub.UserId, ub.BookId }).IsUnique();
        builder.HasIndex(ub => new { ub.UserId, ub.Status, ub.UpdatedAtUtc });
        builder.HasIndex(ub => new { ub.UserId, ub.CompletedOn });
        builder.HasIndex(ub => new { ub.UserId, ub.AddedAtUtc });
        builder.HasIndex(ub => ub.BookId);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(ub => ub.UserId).OnDelete(DeleteBehavior.Cascade);

        // A catalogue book cannot be deleted while any library references it, so reading history never vanishes.
        builder.HasOne(ub => ub.Book).WithMany().HasForeignKey(ub => ub.BookId).OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(ub => ub.Sessions).WithOne(s => s.UserBook).HasForeignKey(s => s.UserBookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(ub => ub.Notes).WithOne(n => n.UserBook).HasForeignKey(n => n.UserBookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(ub => ub.Loans).WithOne(l => l.UserBook).HasForeignKey(l => l.UserBookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(ub => ub.ShelfEntries).WithOne(s => s.UserBook).HasForeignKey(s => s.UserBookId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReadingSessionConfiguration : IEntityTypeConfiguration<ReadingSession>
{
    public void Configure(EntityTypeBuilder<ReadingSession> builder)
    {
        builder.ToTable("ReadingSessions", t =>
        {
            t.HasCheckConstraint("CK_ReadingSessions_Pages", "\"StartPage\" >= 0 AND \"EndPage\" >= \"StartPage\"");
            t.HasCheckConstraint("CK_ReadingSessions_Duration", "\"DurationMinutes\" BETWEEN 0 AND 1440");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Reflection).HasMaxLength(ReadingSession.ReflectionMaxLength);
        builder.Ignore(s => s.PagesRead);

        // Prevents the same session being stored twice (double submit) and supports date-range queries.
        builder.HasIndex(s => new { s.UserBookId, s.ReadOn, s.StartPage, s.EndPage }).IsUnique();
        builder.HasIndex(s => s.ReadOn);
    }
}

internal sealed class ReadingNoteConfiguration : IEntityTypeConfiguration<ReadingNote>
{
    public void Configure(EntityTypeBuilder<ReadingNote> builder)
    {
        builder.ToTable("ReadingNotes", t => t.HasCheckConstraint("CK_ReadingNotes_Page", "\"PageNumber\" IS NULL OR \"PageNumber\" >= 0"));
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Content).HasMaxLength(ReadingNote.ContentMaxLength).IsRequired();
        builder.Property(n => n.Chapter).HasMaxLength(ReadingNote.ChapterMaxLength);
        builder.HasIndex(n => new { n.UserBookId, n.UpdatedAtUtc });
    }
}

internal sealed class ReadingGoalConfiguration : IEntityTypeConfiguration<ReadingGoal>
{
    public void Configure(EntityTypeBuilder<ReadingGoal> builder)
    {
        builder.ToTable("ReadingGoals", t =>
        {
            t.HasCheckConstraint("CK_ReadingGoals_Targets", "\"TargetBooks\" > 0 AND (\"TargetPages\" IS NULL OR \"TargetPages\" > 0)");
            t.HasCheckConstraint("CK_ReadingGoals_Year", "\"Year\" BETWEEN 2000 AND 2100");
        });
        builder.HasKey(g => g.Id);
        builder.HasIndex(g => new { g.UserId, g.Year }).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BookLoanConfiguration : IEntityTypeConfiguration<BookLoan>
{
    public void Configure(EntityTypeBuilder<BookLoan> builder)
    {
        builder.ToTable("BookLoans", t =>
        {
            t.HasCheckConstraint("CK_BookLoans_DueAfterLoan", "\"DueOn\" IS NULL OR \"DueOn\" >= \"LoanedOn\"");
            t.HasCheckConstraint("CK_BookLoans_ReturnAfterLoan", "\"ReturnedOn\" IS NULL OR \"ReturnedOn\" >= \"LoanedOn\"");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.BorrowerName).HasMaxLength(BookLoan.BorrowerMaxLength).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(BookLoan.NotesMaxLength);

        // At most one outstanding loan per copy.
        builder.HasIndex(l => l.UserBookId).IsUnique().HasFilter("\"ReturnedOn\" IS NULL").HasDatabaseName("IX_BookLoans_Outstanding");
        builder.HasIndex(l => new { l.UserBookId, l.LoanedOn });
    }
}

internal sealed class CustomShelfConfiguration : IEntityTypeConfiguration<CustomShelf>
{
    public void Configure(EntityTypeBuilder<CustomShelf> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(CustomShelf.NameMaxLength).IsRequired();
        builder.Property(s => s.NormalizedName).HasMaxLength(CustomShelf.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(CustomShelf.DescriptionMaxLength);
        builder.Property(s => s.ShareSlug).HasMaxLength(40).IsRequired();
        builder.HasIndex(s => new { s.UserId, s.NormalizedName }).IsUnique();
        builder.HasIndex(s => s.ShareSlug).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Entries).WithOne(e => e.Shelf).HasForeignKey(e => e.ShelfId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomShelfBookConfiguration : IEntityTypeConfiguration<CustomShelfBook>
{
    public void Configure(EntityTypeBuilder<CustomShelfBook> builder)
    {
        builder.HasKey(e => new { e.ShelfId, e.UserBookId });
        builder.HasIndex(e => e.UserBookId);
    }
}

internal sealed class ReadingFileConfiguration : IEntityTypeConfiguration<ReadingFile>
{
    public void Configure(EntityTypeBuilder<ReadingFile> builder)
    {
        builder.ToTable("ReadingFiles", t =>
        {
            t.HasCheckConstraint("CK_ReadingFiles_Size", "\"SizeBytes\" > 0");
            t.HasCheckConstraint("CK_ReadingFiles_Percent", "\"LastPercent\" IS NULL OR \"LastPercent\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_ReadingFiles_Page", "\"LastPage\" IS NULL OR \"LastPage\" >= 1");
        });
        builder.HasKey(f => f.Id);
        builder.Property(f => f.StorageKey).HasMaxLength(ReadingFile.StorageKeyMaxLength).IsRequired();
        builder.Property(f => f.FileName).HasMaxLength(ReadingFile.FileNameMaxLength).IsRequired();
        builder.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(f => f.SourceUrl).HasMaxLength(ReadingFile.SourceUrlMaxLength);
        builder.Property(f => f.SourceNote).HasMaxLength(ReadingFile.SourceNoteMaxLength);
        builder.Property(f => f.LastLocation).HasMaxLength(ReadingFile.LocationMaxLength);
        builder.Property(f => f.LastPercent).HasPrecision(5, 2);

        // One reading file per library entry; the file row goes with the entry.
        builder.HasIndex(f => f.UserBookId).IsUnique();
        builder.HasIndex(f => f.StorageKey).IsUnique();
        builder.HasOne(f => f.UserBook).WithOne().HasForeignKey<ReadingFile>(f => f.UserBookId).OnDelete(DeleteBehavior.Cascade);
    }
}
