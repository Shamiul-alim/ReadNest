using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.UnitTests.Domain;

public sealed class ReadingSessionTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static UserBook Entry(int? pages = 300) =>
        UserBook.Create(Guid.NewGuid(), UserBookTests.NewBook(pages), ReadingStatus.WantToRead, Today, Now);

    [Fact]
    public void Pages_read_is_derived_from_the_page_range()
    {
        var session = ReadingSession.Create(Entry(), Today, 40, 75, 30, null, Today, Now);

        Assert.Equal(35, session.PagesRead);
    }

    [Fact]
    public void Session_moves_bookmark_forward_and_starts_book()
    {
        var entry = Entry();
        ReadingSession.Create(entry, Today.AddDays(-2), 0, 50, 20, null, Today, Now);

        Assert.Equal(50, entry.CurrentPage);
        Assert.Equal(ReadingStatus.CurrentlyReading, entry.Status);
        Assert.Equal(Today.AddDays(-2), entry.StartedOn);
        Assert.Equal(Today.AddDays(-2), entry.LastReadOn);
    }

    [Fact]
    public void Older_session_never_moves_bookmark_backwards()
    {
        var entry = Entry();
        entry.UpdateProgress(200, Today, Now);

        ReadingSession.Create(entry, Today.AddDays(-10), 10, 20, 5, null, Today, Now);

        Assert.Equal(200, entry.CurrentPage);
        Assert.Equal(Today, entry.LastReadOn);
        Assert.Equal(Today.AddDays(-10), entry.StartedOn);
    }

    [Fact]
    public void End_page_before_start_page_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => ReadingSession.Create(Entry(), Today, 50, 40, 10, null, Today, Now));
        Assert.Equal("endPage", ex.Field);
    }

    [Fact]
    public void Pages_beyond_the_book_are_rejected()
    {
        Assert.Throws<DomainException>(() => ReadingSession.Create(Entry(300), Today, 290, 310, 10, null, Today, Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Duration_must_be_within_a_day(int minutes)
    {
        var ex = Assert.Throws<DomainException>(() => ReadingSession.Create(Entry(), Today, 0, 10, minutes, null, Today, Now));
        Assert.Equal("durationMinutes", ex.Field);
    }

    [Fact]
    public void Future_sessions_are_rejected()
    {
        Assert.Throws<DomainException>(() => ReadingSession.Create(Entry(), Today.AddDays(1), 0, 10, 10, null, Today, Now));
    }

    [Fact]
    public void Updating_a_session_revalidates_and_keeps_pages_derived()
    {
        var entry = Entry();
        var session = ReadingSession.Create(entry, Today, 0, 10, 10, null, Today, Now);

        session.Update(Today, 10, 60, 25, "Better", Today, Now);

        Assert.Equal(50, session.PagesRead);
        Assert.Equal(60, entry.CurrentPage);
        Assert.Throws<DomainException>(() => session.Update(Today, 60, 10, 25, null, Today, Now));
    }
}

public sealed class BookLoanTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static UserBook OwnedEntry()
    {
        var entry = UserBook.Create(Guid.NewGuid(), UserBookTests.NewBook(), ReadingStatus.Read, Today, Now);
        entry.SetOwnership(OwnershipStatus.Owned, BookFormat.Physical, null, null, null, Today, Now);
        return entry;
    }

    [Fact]
    public void Only_owned_books_can_be_lent()
    {
        var entry = UserBook.Create(Guid.NewGuid(), UserBookTests.NewBook(), ReadingStatus.Read, Today, Now);

        Assert.Throws<DomainException>(() => BookLoan.Create(entry, "Sam", Today, null, null, Today, Now));
    }

    [Fact]
    public void A_copy_can_only_have_one_outstanding_loan()
    {
        var entry = OwnedEntry();
        BookLoan.Create(entry, "Sam", Today, null, null, Today, Now);

        Assert.Throws<DomainException>(() => BookLoan.Create(entry, "Alex", Today, null, null, Today, Now));
    }

    [Fact]
    public void Due_date_before_loan_date_is_rejected()
    {
        Assert.Throws<DomainException>(() => BookLoan.Create(OwnedEntry(), "Sam", Today, Today.AddDays(-1), null, Today, Now));
    }

    [Fact]
    public void Returned_loan_keeps_history_and_allows_a_new_loan()
    {
        var entry = OwnedEntry();
        var loan = BookLoan.Create(entry, "Sam", Today.AddDays(-14), Today.AddDays(-1), null, Today, Now);
        Assert.True(loan.IsOverdue(Today));

        loan.MarkReturned(Today, Today, Now);

        Assert.False(loan.IsOutstanding);
        Assert.False(loan.IsOverdue(Today));
        var second = BookLoan.Create(entry, "Alex", Today, null, null, Today, Now);
        Assert.Equal(2, entry.Loans.Count);
        Assert.True(second.IsOutstanding);
    }

    [Fact]
    public void Return_before_loan_date_is_rejected()
    {
        var loan = BookLoan.Create(OwnedEntry(), "Sam", Today, null, null, Today, Now);

        Assert.Throws<DomainException>(() => loan.MarkReturned(Today.AddDays(-1), Today, Now));
    }
}

public sealed class OtherEntityTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ada", true)]
    [InlineData("ada-lovelace_1", true)]
    [InlineData("ab", false)]
    [InlineData("Ada", false)]
    [InlineData("-ada", false)]
    [InlineData("ada-", false)]
    [InlineData("ada lovelace", false)]
    public void Handle_validation(string handle, bool valid) => Assert.Equal(valid, UserProfile.IsValidHandle(handle));

    [Fact]
    public void Library_cannot_be_public_when_profile_is_private()
    {
        var profile = UserProfile.Create(Guid.NewGuid(), "reader", "Reader", "UTC", Now);
        profile.UpdatePrivacy(isProfilePublic: false, isLibraryPublic: true, Now);

        Assert.False(profile.IsLibraryPublic);
    }

    [Fact]
    public void Users_cannot_follow_themselves()
    {
        var id = Guid.NewGuid();

        Assert.Throws<DomainException>(() => UserFollow.Create(id, id, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Goal_target_must_be_reasonable(int target)
    {
        Assert.Throws<DomainException>(() => ReadingGoal.Create(Guid.NewGuid(), 2026, target, null, Now));
    }

    [Fact]
    public void Shelf_names_are_normalised_for_uniqueness()
    {
        var a = CustomShelf.Create(Guid.NewGuid(), "  Software   Engineering ", null, false, "slug1", Now);
        var b = CustomShelf.Create(Guid.NewGuid(), "software engineering", null, false, "slug2", Now);

        Assert.Equal("Software Engineering", a.Name);
        Assert.Equal(a.NormalizedName, b.NormalizedName);
    }

    [Fact]
    public void Challenge_must_end_after_it_starts()
    {
        Assert.Throws<DomainException>(() =>
            ReadingChallenge.Create(Guid.NewGuid(), "Sprint", null, 3, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1), true, Now));
    }

    [Fact]
    public void Manual_books_require_an_owner()
    {
        Assert.Throws<DomainException>(() => Book.Create(new BookMetadata("Diary"), BookOrigin.Manual, null, Now));
    }

    [Fact]
    public void Book_normalises_isbn10_to_isbn13()
    {
        var book = Book.Create(new BookMetadata("Clean Code", Isbn10: "0-13-235088-2"), BookOrigin.Catalog, null, Now);

        Assert.Equal("9780132350884", book.Isbn13);
        Assert.Equal("0132350882", book.Isbn10);
    }
}
