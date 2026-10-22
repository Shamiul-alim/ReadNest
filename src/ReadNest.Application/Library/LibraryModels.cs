using ReadNest.Domain.Enums;

namespace ReadNest.Application.Library;

public enum LibrarySort
{
    LastUpdated = 0,
    DateAdded = 1,
    Title = 2,
    Author = 3,
    CompletedOn = 4,
    Rating = 5,
}

public sealed record LibraryQuery
{
    public string? Search { get; init; }

    public ReadingStatus? Status { get; init; }

    /// <summary>Convenience filter for the combined "Paused and Did Not Finish" view.</summary>
    public IReadOnlyList<ReadingStatus>? Statuses { get; init; }

    public string? Genre { get; init; }

    public string? Author { get; init; }

    public int? MinRating { get; init; }

    public bool FavoritesOnly { get; init; }

    public Guid? ShelfId { get; init; }

    public LibrarySort Sort { get; init; } = LibrarySort.LastUpdated;

    public bool Descending { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 24;
}

public sealed record LibraryEntryDto(
    Guid Id,
    Guid BookId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    string? CoverUrl,
    ReadingStatus Status,
    int CurrentPage,
    int? TotalPages,
    int? Rating,
    bool IsFavorite,
    OwnershipStatus Ownership,
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    DateOnly? LastReadOn,
    DateTime AddedAtUtc,
    DateTime UpdatedAtUtc,
    string? Isbn13 = null)
{
    public decimal? ProgressPercent => Domain.Entities.UserBook.CalculateProgress(CurrentPage, TotalPages);
}

public sealed record ShelfRefDto(Guid Id, string Name);

public sealed record LibraryEntryDetailsDto(
    Guid Id,
    Guid BookId,
    string Title,
    IReadOnlyList<string> Authors,
    string? CoverUrl,
    ReadingStatus Status,
    int CurrentPage,
    int? TotalPages,
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    DateOnly? LastReadOn,
    int? Rating,
    string? Review,
    ReviewVisibility ReviewVisibility,
    DateTime? ReviewUpdatedAtUtc,
    bool IsFavorite,
    string? PrivateNotes,
    OwnershipStatus Ownership,
    BookFormat? Format,
    DateOnly? AcquiredOn,
    decimal? PurchasePrice,
    string? ConditionNotes,
    DateTime AddedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ShelfRefDto> Shelves,
    int SessionCount,
    int NoteCount,
    bool HasOutstandingLoan,
    string? Isbn13 = null)
{
    public decimal? ProgressPercent => Domain.Entities.UserBook.CalculateProgress(CurrentPage, TotalPages);
}

public sealed record StatusCountsDto(int WantToRead, int CurrentlyReading, int Read, int Paused, int DidNotFinish)
{
    public int Total => WantToRead + CurrentlyReading + Read + Paused + DidNotFinish;

    public int Get(ReadingStatus status) => status switch
    {
        ReadingStatus.WantToRead => WantToRead,
        ReadingStatus.CurrentlyReading => CurrentlyReading,
        ReadingStatus.Read => Read,
        ReadingStatus.Paused => Paused,
        ReadingStatus.DidNotFinish => DidNotFinish,
        _ => 0,
    };
}

public sealed record LibraryFilterOptionsDto(IReadOnlyList<string> Genres, IReadOnlyList<string> Authors);

/// <summary>What would be permanently deleted together with a library entry.</summary>
public sealed record RemovalImpactDto(int Sessions, int Notes, int Loans, int Shelves, bool HasReview, bool HasOutstandingLoan);

/// <summary>Identifies a catalogue record to check against the user's library.</summary>
public sealed record CatalogMatchKey(CatalogProvider Provider, string ExternalId, string? Isbn13);

public sealed record AddToLibraryRequest(Guid BookId, ReadingStatus Status = ReadingStatus.WantToRead);

public sealed record UpdateStatusRequest(ReadingStatus Status, DateOnly? CompletedOn = null);

public sealed record UpdateProgressRequest(int CurrentPage, bool RecordSession = true, int? DurationMinutes = null);

/// <summary>The outcome of a progress update, with what is needed to undo it.</summary>
public sealed record ProgressUpdateResult(LibraryEntryDetailsDto Entry, int PreviousPage, ReadingStatus PreviousStatus, Guid? RecordedSessionId);

public sealed record UpdateEntryDetailsRequest(int? TotalPages, DateOnly? StartedOn, DateOnly? CompletedOn, string? PrivateNotes);

public sealed record AddEntryResult(Guid EntryId, Guid BookId, bool AlreadyInLibrary);
