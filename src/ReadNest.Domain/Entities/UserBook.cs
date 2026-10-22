using ReadNest.Domain.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.Domain.Entities;

/// <summary>
/// A book in one user's personal library together with that user's reading state, rating, review and
/// ownership details. There is at most one entry per (user, book).
/// </summary>
/// <remarks>
/// Status/date invariant: <see cref="CompletedOn"/> is only ever set while <see cref="Status"/> is
/// <see cref="ReadingStatus.Read"/>, and <see cref="StartedOn"/> is never after <see cref="CompletedOn"/>.
/// A read book may have an unknown completion date (for example after a CSV import); such books are not
/// attributed to any year's goal rather than being guessed as "today".
/// </remarks>
public sealed class UserBook : Entity
{
    public const int ReviewMaxLength = 5_000;
    public const int PrivateNotesMaxLength = 4_000;
    public const int ConditionNotesMaxLength = 500;
    public const decimal MaxPurchasePrice = 1_000_000m;

    private UserBook()
    {
    }

    public Guid UserId { get; private set; }

    public Guid BookId { get; private set; }

    public Book Book { get; private set; } = null!;

    public ReadingStatus Status { get; private set; }

    public int CurrentPage { get; private set; }

    /// <summary>
    /// Page count of the edition this user reads. Initialised from the catalogue record but editable,
    /// because the user's edition can differ from the catalogue's.
    /// </summary>
    public int? TotalPages { get; private set; }

    public DateOnly? StartedOn { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public DateOnly? LastReadOn { get; private set; }

    public int? Rating { get; private set; }

    public string? Review { get; private set; }

    public ReviewVisibility ReviewVisibility { get; private set; }

    public DateTime? ReviewUpdatedAtUtc { get; private set; }

    public bool IsFavorite { get; private set; }

    public string? PrivateNotes { get; private set; }

    public OwnershipStatus Ownership { get; private set; }

    public BookFormat? Format { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    public decimal? PurchasePrice { get; private set; }

    public string? ConditionNotes { get; private set; }

    public DateTime AddedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public List<ReadingSession> Sessions { get; private set; } = [];

    public List<ReadingNote> Notes { get; private set; } = [];

    public List<BookLoan> Loans { get; private set; } = [];

    public List<CustomShelfBook> ShelfEntries { get; private set; } = [];

    public decimal? ProgressPercent => CalculateProgress(CurrentPage, TotalPages);

    public static decimal? CalculateProgress(int currentPage, int? totalPages) =>
        totalPages is > 0 ? Math.Round(Math.Min(currentPage, totalPages.Value) * 100m / totalPages.Value, 2) : null;

    public static UserBook Create(Guid userId, Book book, ReadingStatus status, DateOnly today, DateTime nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("A library entry must belong to a user.");
        }

        var entry = new UserBook
        {
            UserId = userId,
            BookId = book.Id,
            Book = book,
            TotalPages = book.PageCount,
            Status = ReadingStatus.WantToRead,
            AddedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        entry.ChangeStatus(status, today, completedOn: null, nowUtc);
        return entry;
    }

    public void ChangeStatus(ReadingStatus status, DateOnly today, DateOnly? completedOn, DateTime nowUtc, bool defaultCompletionToToday = true)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainException("Unknown reading status.", "status");
        }

        if (completedOn is not null && status != ReadingStatus.Read)
        {
            throw new DomainException("A completion date can only be set when the book is marked as read.", "completedOn");
        }

        switch (status)
        {
            case ReadingStatus.WantToRead:
                CompletedOn = null;
                break;

            case ReadingStatus.CurrentlyReading:
            case ReadingStatus.Paused:
            case ReadingStatus.DidNotFinish:
                // Reopening a finished book keeps its sessions; only the completion marker is cleared.
                CompletedOn = null;
                StartedOn ??= today;
                break;

            case ReadingStatus.Read:
                var completion = completedOn
                    ?? (Status == ReadingStatus.Read ? CompletedOn : null)
                    ?? (defaultCompletionToToday ? today : null);
                if (completion is { } completed)
                {
                    Guard.NotInFuture(completed, today, "completedOn", "Completion date");
                    if (StartedOn is { } started && started > completed)
                    {
                        throw new DomainException("The completion date cannot be before the start date.", "completedOn");
                    }

                    StartedOn ??= completed;
                }

                CompletedOn = completion;
                if (TotalPages is { } total)
                {
                    CurrentPage = total;
                }

                break;
        }

        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    public void UpdateProgress(int currentPage, DateOnly today, DateTime nowUtc)
    {
        ValidatePage(currentPage, "currentPage");
        if (currentPage == CurrentPage)
        {
            return;
        }

        CurrentPage = currentPage;
        if (currentPage > 0 && Status is ReadingStatus.WantToRead or ReadingStatus.Paused)
        {
            Status = ReadingStatus.CurrentlyReading;
            StartedOn ??= today;
        }

        if (LastReadOn is null || LastReadOn < today)
        {
            LastReadOn = today;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Sets the bookmark from imported data. Unlike <see cref="UpdateProgress"/> this records no reading activity:
    /// the status and the last-read date are left alone.
    /// </summary>
    public void SetImportedPosition(int currentPage, DateTime nowUtc)
    {
        ValidatePage(currentPage, "currentPage");
        CurrentPage = currentPage;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Synchronises the reading position after a reading session was recorded or edited.</summary>
    internal void ApplySessionProgress(int endPage, DateOnly readOn, DateTime nowUtc)
    {
        if (endPage > CurrentPage)
        {
            CurrentPage = endPage;
        }

        if (LastReadOn is null || readOn > LastReadOn)
        {
            LastReadOn = readOn;
        }

        if (Status is ReadingStatus.WantToRead or ReadingStatus.Paused)
        {
            Status = ReadingStatus.CurrentlyReading;
        }

        // A session dated before the recorded start moves the start back; it can never pass the
        // completion date because StartedOn <= CompletedOn already holds.
        if (StartedOn is null || readOn < StartedOn)
        {
            StartedOn = readOn;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void ValidatePage(int page, string field)
    {
        if (page < 0)
        {
            throw new DomainException("Page numbers cannot be negative.", field);
        }

        if (TotalPages is { } total && page > total)
        {
            throw new DomainException($"Page {page} is beyond the last page of this book ({total}).", field);
        }

        if (page > Book.MaxPageCount)
        {
            throw new DomainException("Page number is too large.", field);
        }
    }

    public void SetTotalPages(int? totalPages, DateTime nowUtc)
    {
        if (totalPages is { } total)
        {
            Guard.Range(total, 1, Book.MaxPageCount, "totalPages", "Total pages");
            if (CurrentPage > total)
            {
                throw new DomainException("Total pages cannot be less than your current page.", "totalPages");
            }

            if (Status == ReadingStatus.Read)
            {
                CurrentPage = total;
            }
        }

        TotalPages = totalPages;
        UpdatedAtUtc = nowUtc;
    }

    public void SetReadingDates(DateOnly? startedOn, DateOnly? completedOn, DateOnly today, DateTime nowUtc)
    {
        if (startedOn is { } start)
        {
            Guard.NotInFuture(start, today, "startedOn", "Start date");
        }

        if (completedOn is { } completion)
        {
            if (Status != ReadingStatus.Read)
            {
                throw new DomainException("A completion date can only be set when the book is marked as read.", "completedOn");
            }

            Guard.NotInFuture(completion, today, "completedOn", "Completion date");
        }

        if (startedOn > completedOn)
        {
            throw new DomainException("The completion date cannot be before the start date.", "completedOn");
        }

        StartedOn = startedOn;
        CompletedOn = completedOn;
        UpdatedAtUtc = nowUtc;
    }

    public void SetRating(int? rating, DateTime nowUtc)
    {
        if (rating is { } value)
        {
            Guard.Range(value, 1, 5, "rating", "Rating");
        }

        Rating = rating;
        UpdatedAtUtc = nowUtc;
    }

    public void SetReview(string? text, ReviewVisibility visibility, DateTime nowUtc)
    {
        if (!Enum.IsDefined(visibility))
        {
            throw new DomainException("Unknown review visibility.", "visibility");
        }

        var review = Guard.Optional(text, ReviewMaxLength, "review", "Review");
        if (review is null && visibility == ReviewVisibility.Public)
        {
            throw new DomainException("Write a review before publishing it.", "review");
        }

        Review = review;
        ReviewVisibility = review is null ? ReviewVisibility.Private : visibility;
        ReviewUpdatedAtUtc = review is null ? null : nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void DeleteReview(DateTime nowUtc)
    {
        Review = null;
        ReviewVisibility = ReviewVisibility.Private;
        ReviewUpdatedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void SetFavorite(bool isFavorite, DateTime nowUtc)
    {
        IsFavorite = isFavorite;
        UpdatedAtUtc = nowUtc;
    }

    public void SetPrivateNotes(string? notes, DateTime nowUtc)
    {
        PrivateNotes = Guard.Optional(notes, PrivateNotesMaxLength, "privateNotes", "Private notes");
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Updates ownership details. Requires <see cref="Loans"/> to be loaded so an outstanding loan can be detected.
    /// </summary>
    public void SetOwnership(
        OwnershipStatus ownership,
        BookFormat? format,
        DateOnly? acquiredOn,
        decimal? purchasePrice,
        string? conditionNotes,
        DateOnly today,
        DateTime nowUtc)
    {
        if (!Enum.IsDefined(ownership) || (format is { } f && !Enum.IsDefined(f)))
        {
            throw new DomainException("Unknown ownership or format.", "ownership");
        }

        if (ownership != OwnershipStatus.Owned && Loans.Any(l => l.IsOutstanding))
        {
            throw new DomainException("This book is currently lent out. Mark the loan as returned before changing ownership.", "ownership");
        }

        if (acquiredOn is { } acquired)
        {
            Guard.NotInFuture(acquired, today, "acquiredOn", "Acquisition date");
        }

        if (purchasePrice is { } price && (price < 0 || price > MaxPurchasePrice))
        {
            throw new DomainException("Purchase price must be between 0 and 1,000,000.", "purchasePrice");
        }

        var owned = ownership == OwnershipStatus.Owned;
        Ownership = ownership;
        Format = format;
        AcquiredOn = owned ? acquiredOn : null;
        PurchasePrice = owned && purchasePrice is { } p ? Math.Round(p, 2) : null;
        ConditionNotes = owned ? Guard.Optional(conditionNotes, ConditionNotesMaxLength, "conditionNotes", "Condition notes") : null;
        UpdatedAtUtc = nowUtc;
    }

    public void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
