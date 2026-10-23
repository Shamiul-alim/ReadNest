using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Library;

/// <summary>
/// The personal library. Every method takes the authenticated user's id (resolved by the web layer from the
/// validated identity) and every query is filtered by it, so entries of other users behave as "not found".
/// </summary>
public sealed class LibraryService(IReadNestDbContextFactory dbFactory, BookCatalogImporter importer, UserClock clock, IFileStore files)
{
    public async Task<PagedResult<LibraryEntryDto>> ListAsync(Guid userId, LibraryQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entries = Filter(db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId), query);
        entries = Sort(entries, query.Sort, query.Descending);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return await entries.Select(LibraryProjections.Entry).ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<LibraryEntryDetailsDto> GetEntryAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await QueryDetails(db, userId).Where(ub => ub.Id == entryId).Select(LibraryProjections.Details).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");
    }

    public async Task<LibraryEntryDetailsDto?> FindEntryForBookAsync(Guid userId, Guid bookId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await QueryDetails(db, userId).Where(ub => ub.BookId == bookId).Select(LibraryProjections.Details).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AddEntryResult> AddBookAsync(Guid userId, AddToLibraryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var book = await BookService.VisibleBooks(db, userId).FirstOrDefaultAsync(b => b.Id == request.BookId, cancellationToken)
            ?? throw new NotFoundException("That book could not be found.");
        return await AddAsync(db, userId, book, request.Status, cancellationToken);
    }

    public async Task<AddEntryResult> AddCatalogBookAsync(Guid userId, CatalogBook source, ReadingStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var bookId = await importer.ImportAsync(db, source, cancellationToken);
        var book = await db.Books.FirstAsync(b => b.Id == bookId, cancellationToken);
        return await AddAsync(db, userId, book, status, cancellationToken);
    }

    public async Task<AddEntryResult> AddManualBookAsync(Guid userId, ManualBookInput input, ReadingStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var bookId = await importer.CreateManualAsync(db, userId, input, cancellationToken);
        var book = await db.Books.FirstAsync(b => b.Id == bookId, cancellationToken);
        return await AddAsync(db, userId, book, status, cancellationToken);
    }

    public async Task<LibraryEntryDetailsDto> UpdateStatusAsync(Guid userId, Guid entryId, UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        entry.ChangeStatus(request.Status, today, request.CompletedOn, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetEntryAsync(userId, entryId, cancellationToken);
    }

    /// <summary>
    /// Moves the bookmark. When the page moves forward and <see cref="UpdateProgressRequest.RecordSession"/> is set,
    /// the pages in between are recorded as today's reading session, so statistics reflect the progress.
    /// </summary>
    public async Task<LibraryEntryDetailsDto> UpdateProgressAsync(Guid userId, Guid entryId, UpdateProgressRequest request, CancellationToken cancellationToken) =>
        (await ApplyProgressAsync(userId, entryId, request, cancellationToken)).Entry;

    /// <summary>
    /// Same as <see cref="UpdateProgressAsync"/>, and also reports what changed so the caller can offer Undo:
    /// the previous page and status, and the session recorded for the new pages (if any).
    /// </summary>
    public async Task<ProgressUpdateResult> ApplyProgressAsync(Guid userId, Guid entryId, UpdateProgressRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var now = clock.UtcNow;
        var previousPage = entry.CurrentPage;
        var previousStatus = entry.Status;
        entry.ValidatePage(request.CurrentPage, "currentPage");

        Guid? recordedSessionId = null;
        if (request.RecordSession && request.CurrentPage > previousPage)
        {
            var duplicate = await db.ReadingSessions.AnyAsync(
                s => s.UserBookId == entry.Id && s.ReadOn == today && s.StartPage == previousPage && s.EndPage == request.CurrentPage,
                cancellationToken);
            if (!duplicate)
            {
                var session = ReadingSession.Create(entry, today, previousPage, request.CurrentPage, request.DurationMinutes ?? 0, null, today, now);
                db.ReadingSessions.Add(session);
                recordedSessionId = session.Id;
            }
        }

        entry.UpdateProgress(request.CurrentPage, today, now);
        await db.SaveChangesAsync(cancellationToken);
        return new ProgressUpdateResult(await GetEntryAsync(userId, entryId, cancellationToken), previousPage, previousStatus, recordedSessionId);
    }

    /// <summary>
    /// Reverses <see cref="ApplyProgressAsync"/>: removes the session it recorded, puts the bookmark back and
    /// restores the previous status.
    /// </summary>
    public async Task<LibraryEntryDetailsDto> UndoProgressAsync(Guid userId, Guid entryId, ProgressUpdateResult change, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var now = clock.UtcNow;
        if (change.RecordedSessionId is { } sessionId)
        {
            var session = await db.ReadingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserBookId == entry.Id, cancellationToken);
            if (session is not null)
            {
                db.ReadingSessions.Remove(session);
            }
        }

        entry.ValidatePage(change.PreviousPage, "currentPage");
        entry.UpdateProgress(change.PreviousPage, today, now);
        if (entry.Status != change.PreviousStatus)
        {
            entry.ChangeStatus(change.PreviousStatus, today, null, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetEntryAsync(userId, entryId, cancellationToken);
    }

    public async Task<LibraryEntryDetailsDto> UpdateDetailsAsync(Guid userId, Guid entryId, UpdateEntryDetailsRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var now = clock.UtcNow;
        entry.SetTotalPages(request.TotalPages, now);
        entry.SetReadingDates(request.StartedOn, request.CompletedOn, today, now);
        entry.SetPrivateNotes(request.PrivateNotes, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetEntryAsync(userId, entryId, cancellationToken);
    }

    public async Task SetFavoriteAsync(Guid userId, Guid entryId, bool isFavorite, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LoadOwnedAsync(db, userId, entryId, cancellationToken);
        entry.SetFavorite(isFavorite, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RemovalImpactDto> GetRemovalImpactAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.UserBooks
            .Where(ub => ub.UserId == userId && ub.Id == entryId)
            .Select(ub => new RemovalImpactDto(
                ub.Sessions.Count,
                ub.Notes.Count,
                ub.Loans.Count,
                ub.ShelfEntries.Count,
                ub.Review != null,
                ub.Loans.Any(l => l.ReturnedOn == null)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");
    }

    /// <summary>
    /// Permanently removes a library entry with its sessions, notes, review, shelf placements, loan history and
    /// attached reading file.
    /// The shared catalogue record is never deleted. Removal is refused while the book is lent out.
    /// </summary>
    public async Task RemoveAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await db.UserBooks
            .Include(ub => ub.Loans)
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.Id == entryId, cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");

        if (entry.Loans.Any(l => l.IsOutstanding))
        {
            throw new ConflictException("This book is currently lent out. Mark the loan as returned before removing it from your library.");
        }

        // The reading file row is removed with the entry (cascade); its stored file is deleted afterwards.
        var fileKey = await db.ReadingFiles.Where(f => f.UserBookId == entry.Id).Select(f => f.StorageKey).FirstOrDefaultAsync(cancellationToken);
        db.UserBooks.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        if (fileKey is not null)
        {
            await files.DeleteAsync(fileKey, CancellationToken.None);
        }
    }

    /// <summary>The reader's current local date (based on the time zone in their profile).</summary>
    public async Task<DateOnly> GetTodayAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await clock.GetTodayAsync(db, userId, cancellationToken);
    }

    public async Task<StatusCountsDto> GetStatusCountsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await GetStatusCountsAsync(db, userId, cancellationToken);
    }

    internal static async Task<StatusCountsDto> GetStatusCountsAsync(IReadNestDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var counts = await db.UserBooks
            .Where(ub => ub.UserId == userId)
            .GroupBy(ub => ub.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        int Get(ReadingStatus status) => counts.GetValueOrDefault(status);
        return new StatusCountsDto(Get(ReadingStatus.WantToRead), Get(ReadingStatus.CurrentlyReading), Get(ReadingStatus.Read), Get(ReadingStatus.Paused), Get(ReadingStatus.DidNotFinish));
    }

    public async Task<LibraryFilterOptionsDto> GetFilterOptionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var genres = await db.UserBooks
            .Where(ub => ub.UserId == userId)
            .SelectMany(ub => ub.Book.Genres.Select(g => g.Genre.Name))
            .Distinct()
            .OrderBy(n => n)
            .Take(200)
            .ToListAsync(cancellationToken);
        var authors = await db.UserBooks
            .Where(ub => ub.UserId == userId)
            .SelectMany(ub => ub.Book.Authors.Select(a => a.Author.Name))
            .Distinct()
            .OrderBy(n => n)
            .Take(300)
            .ToListAsync(cancellationToken);
        return new LibraryFilterOptionsDto(genres, authors);
    }

    /// <summary>
    /// For catalogue search results: which records are already in the user's library (by provider id or ISBN).
    /// Returns the external id mapped to the library entry's status.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ReadingStatus>> GetCatalogMatchesAsync(Guid userId, IReadOnlyList<CatalogMatchKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return new Dictionary<string, ReadingStatus>();
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var externalIds = keys.Select(k => k.ExternalId).Distinct().ToList();
        var isbns = keys.Select(k => Isbn.NormalizeToIsbn13(k.Isbn13)).OfType<string>().Distinct().ToList();

        var rows = await db.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Where(ub => ub.Book.ExternalIds.Any(e => externalIds.Contains(e.ExternalId)) || (ub.Book.Isbn13 != null && isbns.Contains(ub.Book.Isbn13)))
            .Select(ub => new
            {
                ub.Status,
                ub.Book.Isbn13,
                ExternalIds = ub.Book.ExternalIds.Select(e => new { e.Provider, e.ExternalId }).ToList(),
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, ReadingStatus>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var isbn = Isbn.NormalizeToIsbn13(key.Isbn13);
            var match = rows.FirstOrDefault(r =>
                r.ExternalIds.Any(e => e.Provider == key.Provider && e.ExternalId == key.ExternalId)
                || (isbn is not null && r.Isbn13 == isbn));
            if (match is not null)
            {
                result[key.ExternalId] = match.Status;
            }
        }

        return result;
    }

    internal static async Task<UserBook> LoadOwnedAsync(IReadNestDbContext db, Guid userId, Guid entryId, CancellationToken cancellationToken) =>
        await db.UserBooks.FirstOrDefaultAsync(ub => ub.UserId == userId && ub.Id == entryId, cancellationToken)
        ?? throw new NotFoundException("That library entry was not found.");

    private async Task<AddEntryResult> AddAsync(IReadNestDbContext db, Guid userId, Book book, ReadingStatus status, CancellationToken cancellationToken)
    {
        var existing = await db.UserBooks
            .Where(ub => ub.UserId == userId && ub.BookId == book.Id)
            .Select(ub => (Guid?)ub.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } existingId)
        {
            return new AddEntryResult(existingId, book.Id, AlreadyInLibrary: true);
        }

        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var entry = UserBook.Create(userId, book, status, today, clock.UtcNow);
        db.UserBooks.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Double submit: the unique (user, book) index rejected the second insert.
            var id = await db.UserBooks.Where(ub => ub.UserId == userId && ub.BookId == book.Id).Select(ub => ub.Id).FirstAsync(cancellationToken);
            return new AddEntryResult(id, book.Id, AlreadyInLibrary: true);
        }

        return new AddEntryResult(entry.Id, book.Id, AlreadyInLibrary: false);
    }

    private static IQueryable<UserBook> QueryDetails(IReadNestDbContext db, Guid userId) =>
        db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId);

    internal static IQueryable<UserBook> Filter(IQueryable<UserBook> entries, LibraryQuery query)
    {
        if (query.Status is { } status)
        {
            entries = entries.Where(ub => ub.Status == status);
        }
        else if (query.Statuses is { Count: > 0 } statuses)
        {
            entries = entries.Where(ub => statuses.Contains(ub.Status));
        }

        if (InputSanitizer.SearchTerm(query.Search) is { } search)
        {
            var key = TextNormalizer.Key(search);
            var isbn = Isbn.NormalizeToIsbn13(search);
            entries = entries.Where(ub =>
                ub.Book.NormalizedTitle.Contains(key)
                || ub.Book.Authors.Any(a => a.Author.NormalizedName.Contains(key))
                || (isbn != null && ub.Book.Isbn13 == isbn));
        }

        if (InputSanitizer.SearchTerm(query.Genre, "genre") is { } genre)
        {
            var key = TextNormalizer.Key(genre);
            entries = entries.Where(ub => ub.Book.Genres.Any(g => g.Genre.NormalizedName == key));
        }

        if (InputSanitizer.SearchTerm(query.Author, "author") is { } author)
        {
            var key = TextNormalizer.Key(author);
            entries = entries.Where(ub => ub.Book.Authors.Any(a => a.Author.NormalizedName == key));
        }

        if (query.MinRating is { } minRating)
        {
            entries = entries.Where(ub => ub.Rating >= Math.Clamp(minRating, 1, 5));
        }

        if (query.FavoritesOnly)
        {
            entries = entries.Where(ub => ub.IsFavorite);
        }

        if (query.ShelfId is { } shelfId)
        {
            entries = entries.Where(ub => ub.ShelfEntries.Any(s => s.ShelfId == shelfId));
        }

        return entries;
    }

    private static IQueryable<UserBook> Sort(IQueryable<UserBook> entries, LibrarySort sort, bool descending) => (sort, descending) switch
    {
        (LibrarySort.Title, false) => entries.OrderBy(ub => ub.Book.NormalizedTitle).ThenBy(ub => ub.Id),
        (LibrarySort.Title, true) => entries.OrderByDescending(ub => ub.Book.NormalizedTitle).ThenBy(ub => ub.Id),
        (LibrarySort.Author, false) => entries
            .OrderBy(ub => ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.NormalizedName).FirstOrDefault())
            .ThenBy(ub => ub.Book.NormalizedTitle),
        (LibrarySort.Author, true) => entries
            .OrderByDescending(ub => ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.NormalizedName).FirstOrDefault())
            .ThenBy(ub => ub.Book.NormalizedTitle),
        (LibrarySort.DateAdded, false) => entries.OrderBy(ub => ub.AddedAtUtc),
        (LibrarySort.DateAdded, true) => entries.OrderByDescending(ub => ub.AddedAtUtc),
        (LibrarySort.CompletedOn, false) => entries.OrderBy(ub => ub.CompletedOn == null).ThenBy(ub => ub.CompletedOn).ThenBy(ub => ub.Book.NormalizedTitle),
        (LibrarySort.CompletedOn, true) => entries.OrderBy(ub => ub.CompletedOn == null).ThenByDescending(ub => ub.CompletedOn).ThenBy(ub => ub.Book.NormalizedTitle),
        (LibrarySort.Rating, false) => entries.OrderBy(ub => ub.Rating == null).ThenBy(ub => ub.Rating).ThenBy(ub => ub.Book.NormalizedTitle),
        (LibrarySort.Rating, true) => entries.OrderBy(ub => ub.Rating == null).ThenByDescending(ub => ub.Rating).ThenBy(ub => ub.Book.NormalizedTitle),
        (_, false) => entries.OrderBy(ub => ub.UpdatedAtUtc),
        _ => entries.OrderByDescending(ub => ub.UpdatedAtUtc),
    };
}

public static class LibraryProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<UserBook, LibraryEntryDto>> Entry = ub => new LibraryEntryDto(
        ub.Id,
        ub.BookId,
        ub.Book.Title,
        ub.Book.Subtitle,
        ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
        ub.Book.Genres.Select(g => g.Genre.Name).ToList(),
        ub.Book.CoverImageUrl,
        ub.Status,
        ub.CurrentPage,
        ub.TotalPages,
        ub.Rating,
        ub.IsFavorite,
        ub.Ownership,
        ub.StartedOn,
        ub.CompletedOn,
        ub.LastReadOn,
        ub.AddedAtUtc,
        ub.UpdatedAtUtc,
        ub.Book.Isbn13);

    public static readonly System.Linq.Expressions.Expression<Func<UserBook, LibraryEntryDetailsDto>> Details = ub => new LibraryEntryDetailsDto(
        ub.Id,
        ub.BookId,
        ub.Book.Title,
        ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
        ub.Book.CoverImageUrl,
        ub.Status,
        ub.CurrentPage,
        ub.TotalPages,
        ub.StartedOn,
        ub.CompletedOn,
        ub.LastReadOn,
        ub.Rating,
        ub.Review,
        ub.ReviewVisibility,
        ub.ReviewUpdatedAtUtc,
        ub.IsFavorite,
        ub.PrivateNotes,
        ub.Ownership,
        ub.Format,
        ub.AcquiredOn,
        ub.PurchasePrice,
        ub.ConditionNotes,
        ub.AddedAtUtc,
        ub.UpdatedAtUtc,
        ub.ShelfEntries.OrderBy(s => s.Shelf.Name).Select(s => new ShelfRefDto(s.ShelfId, s.Shelf.Name)).ToList(),
        ub.Sessions.Count,
        ub.Notes.Count,
        ub.Loans.Any(l => l.ReturnedOn == null),
        ub.Book.Isbn13);
}
