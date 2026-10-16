using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReadNest.Domain.Entities;
using ReadNest.Infrastructure.Identity;
using ReadNest.Infrastructure.Persistence;

namespace ReadNest.Infrastructure.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books", t =>
        {
            t.HasCheckConstraint("CK_Books_PageCount", "\"PageCount\" IS NULL OR \"PageCount\" > 0");
            t.HasCheckConstraint("CK_Books_ManualOwner", "\"Origin\" <> 'Manual' OR \"CreatedByUserId\" IS NOT NULL");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Title).HasMaxLength(Book.TitleMaxLength).IsRequired();
        builder.Property(b => b.NormalizedTitle).HasMaxLength(Book.TitleMaxLength).IsRequired();
        builder.Property(b => b.Subtitle).HasMaxLength(Book.TitleMaxLength);
        builder.Property(b => b.Description).HasMaxLength(Book.DescriptionMaxLength);
        builder.Property(b => b.Isbn10).HasMaxLength(10);
        builder.Property(b => b.Isbn13).HasMaxLength(13);
        builder.Property(b => b.LanguageCode).HasMaxLength(16);
        builder.Property(b => b.PublishedDate).HasMaxLength(32);
        builder.Property(b => b.Publisher).HasMaxLength(300);
        builder.Property(b => b.CoverImageUrl).HasMaxLength(1000);
        builder.Property(b => b.InfoUrl).HasMaxLength(1000);
        builder.Ignore(b => b.IsPrivate);

        // One shared catalogue record per ISBN-13. Manual (private) books may repeat an ISBN across users.
        builder.HasIndex(b => b.Isbn13).IsUnique().HasFilter("\"Isbn13\" IS NOT NULL AND \"Origin\" = 'Catalog'");
        builder.HasIndex(b => b.NormalizedTitle);
        builder.HasIndex(b => b.CreatedByUserId);

        // Deleting an account deletes the private books it created (their library entries go with the account).
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Authors).WithOne(a => a.Book).HasForeignKey(a => a.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Genres).WithOne(g => g.Book).HasForeignKey(g => g.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.ExternalIds).WithOne(e => e.Book).HasForeignKey(e => e.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(Author.NameMaxLength).IsRequired();
        builder.Property(a => a.NormalizedName).HasMaxLength(Author.NameMaxLength).IsRequired();
        builder.HasIndex(a => a.NormalizedName).IsUnique();
    }
}

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(Genre.NameMaxLength).IsRequired();
        builder.Property(g => g.NormalizedName).HasMaxLength(Genre.NameMaxLength).IsRequired();
        builder.HasIndex(g => g.NormalizedName).IsUnique();
        builder.HasData(SeedData.Genres.Select(g => new { g.Id, g.Name, g.NormalizedName }));
    }
}

internal sealed class BookAuthorConfiguration : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        builder.HasKey(ba => new { ba.BookId, ba.AuthorId });
        builder.HasIndex(ba => ba.AuthorId);
        builder.HasOne(ba => ba.Author).WithMany(a => a.Books).HasForeignKey(ba => ba.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BookGenreConfiguration : IEntityTypeConfiguration<BookGenre>
{
    public void Configure(EntityTypeBuilder<BookGenre> builder)
    {
        builder.HasKey(bg => new { bg.BookId, bg.GenreId });
        builder.HasIndex(bg => bg.GenreId);
        builder.HasOne(bg => bg.Genre).WithMany(g => g.Books).HasForeignKey(bg => bg.GenreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BookExternalIdConfiguration : IEntityTypeConfiguration<BookExternalId>
{
    public void Configure(EntityTypeBuilder<BookExternalId> builder)
    {
        // External ids are only unique within their provider.
        builder.HasKey(e => new { e.Provider, e.ExternalId });
        builder.Property(e => e.ExternalId).HasMaxLength(BookExternalId.ExternalIdMaxLength);
        builder.HasIndex(e => e.BookId);
    }
}
