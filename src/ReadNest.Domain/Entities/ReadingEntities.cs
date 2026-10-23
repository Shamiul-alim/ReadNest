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

/// <summary>
/// A private PDF or EPUB attached to a library entry, plus the reader's position in it. The file itself lives
/// in file storage under <see cref="StorageKey"/>; the key is generated by the server and never derived from
/// the uploaded file name. Each library entry has at most one reading file.
/// </summary>
public sealed class ReadingFile : Entity
{
    public const int StorageKeyMaxLength = 200;
    public const int FileNameMaxLength = 255;
    public const int SourceUrlMaxLength = 1_000;
    public const int SourceNoteMaxLength = 500;
    public const int LocationMaxLength = 2_000;

    private ReadingFile()
    {
    }

    public Guid UserBookId { get; private set; }

    public UserBook UserBook { get; private set; } = null!;

    public ReadingFileFormat Format { get; private set; }

    public ReadingFileSource Source { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Display name only (sanitised); it is never used to build a path.</summary>
    public string FileName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the stored file.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    /// <summary>Where an imported file came from (for example its Project Gutenberg page), for attribution.</summary>
    public string? SourceUrl { get; private set; }

    /// <summary>Licence or attribution note shown in the reader.</summary>
    public string? SourceNote { get; private set; }

    public DateTime UploadedAtUtc { get; private set; }

    /// <summary>The last position: an EPUB CFI or a PDF page number, as reported by the reader.</summary>
    public string? LastLocation { get; private set; }

    /// <summary>How far through the file the reader is, 0-100.</summary>
    public decimal? LastPercent { get; private set; }

    /// <summary>The last PDF page shown (1-based). Not used for EPUB files.</summary>
    public int? LastPage { get; private set; }

    /// <summary>Number of pages of a PDF, once the reader has opened it.</summary>
    public int? PageCount { get; private set; }

    public DateTime? LastOpenedAtUtc { get; private set; }

    public DateTime? PositionUpdatedAtUtc { get; private set; }

    public static ReadingFile Create(
        UserBook entry,
        ReadingFileFormat format,
        ReadingFileSource source,
        string storageKey,
        string fileName,
        long sizeBytes,
        string sha256,
        string? sourceUrl,
        string? sourceNote,
        DateTime nowUtc)
    {
        var file = new ReadingFile { UserBookId = entry.Id, UserBook = entry };
        file.Replace(format, source, storageKey, fileName, sizeBytes, sha256, sourceUrl, sourceNote, nowUtc);
        return file;
    }

    /// <summary>
    /// Points the entry at a new file and returns the storage key of the file it replaces. The saved position
    /// belongs to the old file, so it is cleared.
    /// </summary>
    public string Replace(
        ReadingFileFormat format,
        ReadingFileSource source,
        string storageKey,
        string fileName,
        long sizeBytes,
        string sha256,
        string? sourceUrl,
        string? sourceNote,
        DateTime nowUtc)
    {
        if (!Enum.IsDefined(format) || !Enum.IsDefined(source))
        {
            throw new DomainException("Unsupported reading file.", "file");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException("The file is empty.", "file");
        }

        if (sha256.Length != 64)
        {
            throw new DomainException("Invalid file checksum.", "file");
        }

        var previous = StorageKey;
        Format = format;
        Source = source;
        StorageKey = Guard.Required(storageKey, StorageKeyMaxLength, "storageKey", "Storage key");
        FileName = Guard.Required(fileName, FileNameMaxLength, "fileName", "File name");
        SizeBytes = sizeBytes;
        Sha256 = sha256.ToLowerInvariant();
        SourceUrl = Guard.Optional(sourceUrl, SourceUrlMaxLength, "sourceUrl", "Source link");
        SourceNote = Guard.Optional(sourceNote, SourceNoteMaxLength, "sourceNote", "Source note");
        UploadedAtUtc = nowUtc;
        LastLocation = null;
        LastPercent = null;
        LastPage = null;
        PageCount = null;
        LastOpenedAtUtc = null;
        PositionUpdatedAtUtc = null;
        return previous;
    }

    public void MarkOpened(DateTime nowUtc) => LastOpenedAtUtc = nowUtc;

    /// <summary>Saves where the reader is. This is a bookmark only: it never records reading activity.</summary>
    public void UpdatePosition(string? location, decimal? percent, int? page, int? pageCount, DateTime nowUtc)
    {
        if (percent is { } p && (p < 0 || p > 100))
        {
            throw new DomainException("Progress must be between 0 and 100 percent.", "percent");
        }

        if (pageCount is { } count && (count < 1 || count > Book.MaxPageCount))
        {
            throw new DomainException("Page count is out of range.", "pageCount");
        }

        if (page is { } current && (current < 1 || current > (pageCount ?? PageCount ?? Book.MaxPageCount)))
        {
            throw new DomainException("Page is out of range.", "page");
        }

        if (Format == ReadingFileFormat.Pdf && location is not null && !int.TryParse(location, out _))
        {
            throw new DomainException("A PDF position is a page number.", "location");
        }

        LastLocation = Guard.Optional(location, LocationMaxLength, "location", "Position");
        LastPercent = percent is { } value ? Math.Round(value, 2, MidpointRounding.AwayFromZero) : null;
        LastPage = Format == ReadingFileFormat.Pdf ? page : null;
        PageCount = Format == ReadingFileFormat.Pdf ? pageCount ?? PageCount : null;
        PositionUpdatedAtUtc = nowUtc;
    }
}
