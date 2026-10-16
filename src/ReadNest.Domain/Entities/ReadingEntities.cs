using ReadNest.Domain.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.Domain.Entities;

/// <summary>
/// One sitting of reading. Pages read are always derived from the page range (<see cref="PagesRead"/>) so
/// a session can never report a page count that disagrees with its start and end pages.
/// </summary>
public sealed class ReadingSession : Entity
{
    public const int ReflectionMaxLength = 2_000;
    public const int MaxDurationMinutes = 24 * 60;

    private ReadingSession()
    {
    }

    public Guid UserBookId { get; private set; }

    public UserBook UserBook { get; private set; } = null!;

    /// <summary>The reader's local calendar date of the session.</summary>
    public DateOnly ReadOn { get; private set; }

    public int StartPage { get; private set; }

    public int EndPage { get; private set; }

    public int DurationMinutes { get; private set; }

    public string? Reflection { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public int PagesRead => EndPage - StartPage;

    public static ReadingSession Create(
        UserBook entry,
        DateOnly readOn,
        int startPage,
        int endPage,
        int durationMinutes,
        string? reflection,
        DateOnly today,
        DateTime nowUtc)
    {
        var session = new ReadingSession
        {
            UserBookId = entry.Id,
            UserBook = entry,
            CreatedAtUtc = nowUtc,
        };
        session.Apply(entry, readOn, startPage, endPage, durationMinutes, reflection, today, nowUtc);
        return session;
    }

    public void Update(DateOnly readOn, int startPage, int endPage, int durationMinutes, string? reflection, DateOnly today, DateTime nowUtc) =>
        Apply(UserBook, readOn, startPage, endPage, durationMinutes, reflection, today, nowUtc);

    private void Apply(UserBook entry, DateOnly readOn, int startPage, int endPage, int durationMinutes, string? reflection, DateOnly today, DateTime nowUtc)
    {
        Guard.NotInFuture(readOn, today, "readOn", "Reading date");
        entry.ValidatePage(startPage, "startPage");
        entry.ValidatePage(endPage, "endPage");
        if (endPage < startPage)
        {
            throw new DomainException("The ending page must be greater than or equal to the starting page.", "endPage");
        }

        if (durationMinutes < 0 || durationMinutes > MaxDurationMinutes)
        {
            throw new DomainException($"Reading duration must be between 0 and {MaxDurationMinutes} minutes.", "durationMinutes");
        }

        ReadOn = readOn;
        StartPage = startPage;
        EndPage = endPage;
        DurationMinutes = durationMinutes;
        Reflection = Guard.Optional(reflection, ReflectionMaxLength, "reflection", "Reflection");
        UpdatedAtUtc = nowUtc;
        entry.ApplySessionProgress(endPage, readOn, nowUtc);
    }
}

/// <summary>A private note, quote, takeaway, lesson or action item attached to a library entry.</summary>
public sealed class ReadingNote : Entity
{
    public const int ContentMaxLength = 5_000;
    public const int ChapterMaxLength = 200;

    private ReadingNote()
    {
    }

    public Guid UserBookId { get; private set; }

    public UserBook UserBook { get; private set; } = null!;

    public NoteType Type { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public int? PageNumber { get; private set; }

    public string? Chapter { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static ReadingNote Create(UserBook entry, NoteType type, string content, int? pageNumber, string? chapter, DateTime nowUtc)
    {
        var note = new ReadingNote { UserBookId = entry.Id, UserBook = entry, CreatedAtUtc = nowUtc };
        note.Update(type, content, pageNumber, chapter, nowUtc);
        return note;
    }

    public void Update(NoteType type, string content, int? pageNumber, string? chapter, DateTime nowUtc)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("Unknown note type.", "type");
        }

        if (pageNumber is { } page)
        {
            Guard.Range(page, 0, Book.MaxPageCount, "pageNumber", "Page");
        }

        Type = type;
        Content = Guard.Required(content, ContentMaxLength, "content", "Note");
        PageNumber = pageNumber;
        Chapter = Guard.Optional(chapter, ChapterMaxLength, "chapter", "Chapter");
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>A yearly reading target. One goal per user and year.</summary>
public sealed class ReadingGoal : Entity
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    private ReadingGoal()
    {
    }

    public Guid UserId { get; private set; }

    public int Year { get; private set; }

    public int TargetBooks { get; private set; }

    public int? TargetPages { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static ReadingGoal Create(Guid userId, int year, int targetBooks, int? targetPages, DateTime nowUtc)
    {
        var goal = new ReadingGoal
        {
            UserId = userId,
            Year = Guard.Range(year, MinYear, MaxYear, "year", "Year"),
            CreatedAtUtc = nowUtc,
        };
        goal.Update(targetBooks, targetPages, nowUtc);
        return goal;
    }

    public void Update(int targetBooks, int? targetPages, DateTime nowUtc)
    {
        TargetBooks = Guard.Range(targetBooks, 1, 1_000, "targetBooks", "Target books");
        TargetPages = targetPages is { } pages ? Guard.Range(pages, 1, 1_000_000, "targetPages", "Target pages") : null;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>A record of lending an owned book to someone. Outstanding while <see cref="ReturnedOn"/> is empty.</summary>
public sealed class BookLoan : Entity
{
    public const int BorrowerMaxLength = 200;
    public const int NotesMaxLength = 1_000;

    private BookLoan()
    {
    }

    public Guid UserBookId { get; private set; }

    public UserBook UserBook { get; private set; } = null!;

    public string BorrowerName { get; private set; } = string.Empty;

    public DateOnly LoanedOn { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public DateOnly? ReturnedOn { get; private set; }

    public string? Notes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsOutstanding => ReturnedOn is null;

    public bool IsOverdue(DateOnly today) => IsOutstanding && DueOn is { } due && due < today;

    /// <summary>Creates a loan. Requires <see cref="UserBook.Loans"/> to be loaded.</summary>
    public static BookLoan Create(UserBook entry, string borrowerName, DateOnly loanedOn, DateOnly? dueOn, string? notes, DateOnly today, DateTime nowUtc)
    {
        if (entry.Ownership != OwnershipStatus.Owned)
        {
            throw new DomainException("Only books you own can be lent. Mark the book as owned first.", "ownership");
        }

        if (entry.Loans.Any(l => l.IsOutstanding))
        {
            throw new DomainException("This book is already lent out. Mark the current loan as returned first.");
        }

        var loan = new BookLoan { UserBookId = entry.Id, UserBook = entry, CreatedAtUtc = nowUtc };
        loan.Update(borrowerName, loanedOn, dueOn, notes, today, nowUtc);
        entry.Loans.Add(loan);
        return loan;
    }

    public void Update(string borrowerName, DateOnly loanedOn, DateOnly? dueOn, string? notes, DateOnly today, DateTime nowUtc)
    {
        Guard.NotInFuture(loanedOn, today, "loanedOn", "Loan date");
        if (dueOn < loanedOn)
        {
            throw new DomainException("The expected return date cannot be before the loan date.", "dueOn");
        }

        if (ReturnedOn < loanedOn)
        {
            throw new DomainException("The loan date cannot be after the return date.", "loanedOn");
        }

        BorrowerName = Guard.Required(borrowerName, BorrowerMaxLength, "borrowerName", "Borrower name");
        LoanedOn = loanedOn;
        DueOn = dueOn;
        Notes = Guard.Optional(notes, NotesMaxLength, "notes", "Notes");
        UpdatedAtUtc = nowUtc;
    }

    public void MarkReturned(DateOnly returnedOn, DateOnly today, DateTime nowUtc)
    {
        if (!IsOutstanding)
        {
            throw new DomainException("This loan has already been marked as returned.");
        }

        Guard.NotInFuture(returnedOn, today, "returnedOn", "Return date");
        if (returnedOn < LoanedOn)
        {
            throw new DomainException("The return date cannot be before the loan date.", "returnedOn");
        }

        ReturnedOn = returnedOn;
        UpdatedAtUtc = nowUtc;
    }
}
