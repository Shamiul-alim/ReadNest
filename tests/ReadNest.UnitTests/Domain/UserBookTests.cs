using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.UnitTests.Domain;

public sealed class UserBookTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    internal static Book NewBook(int? pages = 464) =>
        Book.Create(new BookMetadata("Clean Code", PageCount: pages, Isbn13: "9780132350884"), BookOrigin.Catalog, null, Now);

    private static UserBook NewEntry(ReadingStatus status = ReadingStatus.WantToRead, int? pages = 464) =>
        UserBook.Create(Guid.NewGuid(), NewBook(pages), status, Today, Now);

    [Fact]
    public void New_entry_copies_page_count_and_starts_unread()
    {
        var entry = NewEntry();

        Assert.Equal(464, entry.TotalPages);
        Assert.Equal(0, entry.CurrentPage);
        Assert.Null(entry.StartedOn);
        Assert.Null(entry.CompletedOn);
    }

    [Fact]
    public void Progress_percentage_is_rounded_to_two_decimals()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);
        entry.UpdateProgress(180, Today, Now);

        Assert.Equal(38.79m, entry.ProgressPercent);
    }

    [Fact]
    public void Progress_percentage_is_null_when_total_pages_unknown()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading, pages: null);
        entry.UpdateProgress(50, Today, Now);

        Assert.Null(entry.ProgressPercent);
        Assert.Equal(50, entry.CurrentPage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(465)]
    public void Progress_outside_the_book_is_rejected(int page)
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);

        var ex = Assert.Throws<DomainException>(() => entry.UpdateProgress(page, Today, Now));
        Assert.Equal("currentPage", ex.Field);
    }

    [Fact]
    public void Moving_the_bookmark_starts_a_want_to_read_book()
    {
        var entry = NewEntry();
        entry.UpdateProgress(10, Today, Now);

        Assert.Equal(ReadingStatus.CurrentlyReading, entry.Status);
        Assert.Equal(Today, entry.StartedOn);
        Assert.Equal(Today, entry.LastReadOn);
    }

    [Fact]
    public void Marking_read_sets_completion_date_and_last_page()
    {
        var entry = NewEntry();
        entry.ChangeStatus(ReadingStatus.Read, Today, new DateOnly(2026, 10, 10), Now);

        Assert.Equal(ReadingStatus.Read, entry.Status);
        Assert.Equal(new DateOnly(2026, 10, 10), entry.CompletedOn);
        Assert.Equal(new DateOnly(2026, 10, 10), entry.StartedOn);
        Assert.Equal(464, entry.CurrentPage);
        Assert.Equal(100m, entry.ProgressPercent);
    }

    [Fact]
    public void Marking_read_without_a_date_uses_today()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);
        entry.ChangeStatus(ReadingStatus.Read, Today, null, Now);

        Assert.Equal(Today, entry.CompletedOn);
    }

    [Fact]
    public void Import_can_mark_read_with_unknown_completion_date()
    {
        var entry = NewEntry();
        entry.ChangeStatus(ReadingStatus.Read, Today, null, Now, defaultCompletionToToday: false);

        Assert.Equal(ReadingStatus.Read, entry.Status);
        Assert.Null(entry.CompletedOn);
    }

    [Fact]
    public void Completion_date_in_the_future_is_rejected()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);

        Assert.Throws<DomainException>(() => entry.ChangeStatus(ReadingStatus.Read, Today, Today.AddDays(1), Now));
    }

    [Fact]
    public void Completion_before_start_is_rejected()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);

        var ex = Assert.Throws<DomainException>(() => entry.ChangeStatus(ReadingStatus.Read, Today, Today.AddDays(-30), Now));
        Assert.Equal("completedOn", ex.Field);
    }

    [Fact]
    public void Completion_date_is_only_allowed_with_status_read()
    {
        var entry = NewEntry();

        Assert.Throws<DomainException>(() => entry.ChangeStatus(ReadingStatus.Paused, Today, Today, Now));
    }

    [Fact]
    public void Reopening_a_finished_book_clears_completion_but_keeps_sessions()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);
        var session = ReadingSession.Create(entry, Today, 0, 100, 30, null, Today, Now);
        entry.Sessions.Add(session);
        entry.ChangeStatus(ReadingStatus.Read, Today, Today, Now);

        entry.ChangeStatus(ReadingStatus.CurrentlyReading, Today, null, Now);

        Assert.Null(entry.CompletedOn);
        Assert.Equal(Today, entry.StartedOn);
        Assert.Single(entry.Sessions);
    }

    [Fact]
    public void Re_marking_read_keeps_the_existing_completion_date()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);
        entry.ChangeStatus(ReadingStatus.Read, Today, Today, Now);

        entry.ChangeStatus(ReadingStatus.Read, Today.AddDays(5), null, Now);

        Assert.Equal(Today, entry.CompletedOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_between_one_and_five(int rating)
    {
        var entry = NewEntry();

        Assert.Throws<DomainException>(() => entry.SetRating(rating, Now));
    }

    [Fact]
    public void Rating_can_be_cleared()
    {
        var entry = NewEntry();
        entry.SetRating(4, Now);
        entry.SetRating(null, Now);

        Assert.Null(entry.Rating);
    }

    [Fact]
    public void Public_review_requires_text()
    {
        var entry = NewEntry();

        Assert.Throws<DomainException>(() => entry.SetReview("   ", ReviewVisibility.Public, Now));
    }

    [Fact]
    public void Review_longer_than_limit_is_rejected()
    {
        var entry = NewEntry();

        Assert.Throws<DomainException>(() => entry.SetReview(new string('a', UserBook.ReviewMaxLength + 1), ReviewVisibility.Private, Now));
    }

    [Fact]
    public void Deleting_review_keeps_rating_and_makes_it_private()
    {
        var entry = NewEntry();
        entry.SetRating(5, Now);
        entry.SetReview("Great", ReviewVisibility.Public, Now);

        entry.DeleteReview(Now);

        Assert.Equal(5, entry.Rating);
        Assert.Null(entry.Review);
        Assert.Equal(ReviewVisibility.Private, entry.ReviewVisibility);
    }

    [Fact]
    public void Total_pages_cannot_drop_below_current_page()
    {
        var entry = NewEntry(ReadingStatus.CurrentlyReading);
        entry.UpdateProgress(300, Today, Now);

        Assert.Throws<DomainException>(() => entry.SetTotalPages(200, Now));
    }

    [Fact]
    public void Changing_ownership_is_refused_while_lent_out()
    {
        var entry = NewEntry();
        entry.SetOwnership(OwnershipStatus.Owned, BookFormat.Physical, null, 25m, null, Today, Now);
        BookLoan.Create(entry, "Sam", Today, null, null, Today, Now);

        Assert.Throws<DomainException>(() => entry.SetOwnership(OwnershipStatus.NotOwned, null, null, null, null, Today, Now));
    }

    [Fact]
    public void Not_owned_books_drop_purchase_details()
    {
        var entry = NewEntry();
        entry.SetOwnership(OwnershipStatus.Owned, BookFormat.Physical, Today, 19.999m, "Signed", Today, Now);
        Assert.Equal(20.00m, entry.PurchasePrice);

        entry.SetOwnership(OwnershipStatus.NotOwned, BookFormat.EBook, Today, 10m, "x", Today, Now);

        Assert.Null(entry.PurchasePrice);
        Assert.Null(entry.AcquiredOn);
        Assert.Null(entry.ConditionNotes);
        Assert.Equal(BookFormat.EBook, entry.Format);
    }
}
