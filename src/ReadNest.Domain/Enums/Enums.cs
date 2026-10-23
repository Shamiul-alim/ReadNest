namespace ReadNest.Domain.Enums;

public enum ReadingStatus
{
    WantToRead = 0,
    CurrentlyReading = 1,
    Read = 2,
    Paused = 3,
    DidNotFinish = 4,
}

public enum NoteType
{
    General = 0,
    Quote = 1,
    Takeaway = 2,
    Lesson = 3,
    ActionItem = 4,
}

public enum OwnershipStatus
{
    NotOwned = 0,
    Owned = 1,
    Borrowed = 2,
}

public enum BookFormat
{
    Physical = 0,
    EBook = 1,
    Audiobook = 2,
}

public enum ReviewVisibility
{
    Private = 0,
    Public = 1,
}

public enum CatalogProvider
{
    GoogleBooks = 1,
    OpenLibrary = 2,
}

public enum BookOrigin
{
    /// <summary>Imported from an external catalogue provider; shared by every user.</summary>
    Catalog = 0,

    /// <summary>Entered manually or imported from CSV; visible only to the user who created it.</summary>
    Manual = 1,
}

/// <summary>Formats ReadNest can render in its own reader.</summary>
public enum ReadingFileFormat
{
    Pdf = 1,
    Epub = 2,
}

/// <summary>Where a reading file came from.</summary>
public enum ReadingFileSource
{
    /// <summary>Uploaded by the reader, who is responsible for having the right to use it.</summary>
    Upload = 1,

    /// <summary>Imported from Project Gutenberg (public domain in the United States).</summary>
    ProjectGutenberg = 2,
}

public enum ReaderTheme
{
    Paper = 0,
    Sepia = 1,
    Night = 2,
}

public enum ReaderWidth
{
    Narrow = 0,
    Medium = 1,
    Wide = 2,
}

public enum ReaderFlow
{
    Paginated = 0,
    Scrolled = 1,
}

public static class EnumDisplayExtensions
{
    public static string ToDisplayName(this ReadingStatus status) => status switch
    {
        ReadingStatus.WantToRead => "Want to read",
        ReadingStatus.CurrentlyReading => "Currently reading",
        ReadingStatus.Read => "Read",
        ReadingStatus.Paused => "Paused",
        ReadingStatus.DidNotFinish => "Did not finish",
        _ => status.ToString(),
    };

    public static string ToDisplayName(this NoteType type) => type switch
    {
        NoteType.General => "Note",
        NoteType.Quote => "Quote",
        NoteType.Takeaway => "Key takeaway",
        NoteType.Lesson => "Lesson",
        NoteType.ActionItem => "Action item",
        _ => type.ToString(),
    };

    public static string ToDisplayName(this BookFormat format) => format switch
    {
        BookFormat.Physical => "Physical",
        BookFormat.EBook => "E-book",
        BookFormat.Audiobook => "Audiobook",
        _ => format.ToString(),
    };

    public static string ToDisplayName(this OwnershipStatus ownership) => ownership switch
    {
        OwnershipStatus.NotOwned => "Not owned",
        OwnershipStatus.Owned => "Owned",
        OwnershipStatus.Borrowed => "Borrowed from someone",
        _ => ownership.ToString(),
    };

    public static string ToDisplayName(this CatalogProvider provider) => provider switch
    {
        CatalogProvider.GoogleBooks => "Google Books",
        CatalogProvider.OpenLibrary => "Open Library",
        _ => provider.ToString(),
    };

    public static string ToDisplayName(this ReadingFileFormat format) => format switch
    {
        ReadingFileFormat.Pdf => "PDF",
        ReadingFileFormat.Epub => "EPUB",
        _ => format.ToString(),
    };

    public static string ToDisplayName(this ReaderTheme theme) => theme switch
    {
        ReaderTheme.Paper => "Paper",
        ReaderTheme.Sepia => "Sepia",
        ReaderTheme.Night => "Night",
        _ => theme.ToString(),
    };

    public static string ToDisplayName(this ReaderWidth width) => width switch
    {
        ReaderWidth.Narrow => "Narrow",
        ReaderWidth.Medium => "Medium",
        ReaderWidth.Wide => "Wide",
        _ => width.ToString(),
    };

    public static string ToDisplayName(this ReaderFlow flow) => flow switch
    {
        ReaderFlow.Paginated => "Page by page",
        ReaderFlow.Scrolled => "Continuous scroll",
        _ => flow.ToString(),
    };
}
