using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.ImportExport;

public sealed record ImportRowError(int LineNumber, string Title, IReadOnlyList<string> Errors);

public sealed record ImportSkippedRow(int LineNumber, string Title, string Reason);

public sealed record ImportSummary(int TotalRows, int Imported, IReadOnlyList<ImportSkippedRow> Skipped, IReadOnlyList<ImportRowError> Errors);

/// <summary>
/// CSV/JSON export and CSV import of the authenticated user's own data. Imported files are treated as untrusted:
/// size and row limits, strict per-row validation, and no field of the file can influence which user the data
/// belongs to. Imported rows never create reading sessions, so statistics are not inflated by imports.
/// </summary>
public sealed class ImportExportService(
    IReadNestDbContextFactory dbFactory,
    BookCatalogImporter importer,
    UserClock clock,
    ILogger<ImportExportService> logger)
{
    public const long MaxImportBytes = 1024 * 1024;
    public const int MaxImportRows = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<string> ExportCsvAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var rows = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .OrderBy(ub => ub.Book.NormalizedTitle)
            .Select(ub => new
            {
                ub.Book.Title,
                Authors = ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                ub.Book.Isbn13,
                ub.Status,
                ub.CurrentPage,
                ub.TotalPages,
                ub.Rating,
                ub.StartedOn,
                ub.CompletedOn,
                ub.IsFavorite,
                Shelves = ub.ShelfEntries.Select(s => s.Shelf.Name).OrderBy(n => n).ToList(),
            })
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.Append(CsvWriter.Line(LibraryCsvFormat.Columns)).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(CsvWriter.Line(
            [
                row.Title,
                string.Join("; ", row.Authors),
                row.Isbn13,
                LibraryCsvFormat.StatusToCsv(row.Status),
                row.CurrentPage.ToString(CultureInfo.InvariantCulture),
                row.TotalPages?.ToString(CultureInfo.InvariantCulture),
                row.Rating?.ToString(CultureInfo.InvariantCulture),
                row.StartedOn?.ToString(LibraryCsvFormat.DateFormat, CultureInfo.InvariantCulture),
                row.CompletedOn?.ToString(LibraryCsvFormat.DateFormat, CultureInfo.InvariantCulture),
                row.IsFavorite ? "true" : "false",
                string.Join("; ", row.Shelves),
            ])).Append("\r\n");
        }

        return builder.ToString();
    }

    public async Task<byte[]> ExportJsonAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Handle, p.DisplayName, p.Bio, p.ProfileImageUrl, p.PreferredLanguage, p.TimeZoneId, p.IsProfilePublic, p.IsLibraryPublic })
            .FirstOrDefaultAsync(cancellationToken);

        var library = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .OrderBy(ub => ub.Book.NormalizedTitle)
            .Select(ub => new
            {
                Book = new
                {
                    ub.Book.Title,
                    ub.Book.Subtitle,
                    Authors = ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                    Genres = ub.Book.Genres.Select(g => g.Genre.Name).ToList(),
                    ub.Book.Isbn13,
                    ub.Book.Isbn10,
                    ub.Book.Publisher,
                    ub.Book.PublishedDate,
                    ub.Book.PageCount,
                    ub.Book.Origin,
                },
                ub.Status,
                ub.CurrentPage,
                ub.TotalPages,
                ub.StartedOn,
                ub.CompletedOn,
                ub.LastReadOn,
                ub.Rating,
                ub.Review,
                ub.ReviewVisibility,
                ub.IsFavorite,
                ub.PrivateNotes,
                ub.Ownership,
                ub.Format,
                ub.AcquiredOn,
                ub.PurchasePrice,
                ub.ConditionNotes,
                ub.AddedAtUtc,
                Sessions = ub.Sessions.OrderBy(s => s.ReadOn)
                    .Select(s => new { s.ReadOn, s.StartPage, s.EndPage, PagesRead = s.EndPage - s.StartPage, s.DurationMinutes, s.Reflection })
                    .ToList(),
                Notes = ub.Notes.OrderBy(n => n.CreatedAtUtc)
                    .Select(n => new { n.Type, n.Content, n.PageNumber, n.Chapter, n.CreatedAtUtc, n.UpdatedAtUtc })
                    .ToList(),
                Loans = ub.Loans.OrderBy(l => l.LoanedOn)
                    .Select(l => new { l.BorrowerName, l.LoanedOn, l.DueOn, l.ReturnedOn, l.Notes })
                    .ToList(),
                Shelves = ub.ShelfEntries.Select(s => s.Shelf.Name).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var shelves = await db.CustomShelves.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Description, s.IsPublic }).ToListAsync(cancellationToken);
        var goals = await db.ReadingGoals.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.Year)
            .Select(g => new { g.Year, g.TargetBooks, g.TargetPages }).ToListAsync(cancellationToken);

        var document = new
        {
            Format = "readnest-export",
            Version = 1,
            ExportedAtUtc = clock.UtcNow,
            Profile = profile,
            Library = library,
            Shelves = shelves,
            Goals = goals,
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public async Task<ImportSummary> ImportCsvAsync(Guid userId, Stream content, CancellationToken cancellationToken)
    {
        var text = await ReadLimitedAsync(content, cancellationToken);
        List<string[]> records;
        try
        {
            records = CsvReader.Parse(text, MaxImportRows + 1);
        }
        catch (CsvFormatException ex)
        {
            throw new RequestValidationException("file", $"The CSV file could not be read: {ex.Message}");
        }

        if (records.Count == 0)
        {
            throw new RequestValidationException("file", "The CSV file is empty.");
        }

        var header = LibraryCsvFormat.ParseHeader(records[0]);
        if (!header.ContainsKey("Title"))
        {
            throw new RequestValidationException("file", "The first row must be a header containing at least a 'Title' column.");
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var errors = new List<ImportRowError>();
        var skipped = new List<ImportSkippedRow>();
        var importedBookIds = new HashSet<Guid>();
        var imported = 0;

        for (var i = 1; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineNumber = i + 1;
            var (row, rowErrors) = LibraryCsvFormat.ValidateRow(header, records[i], lineNumber, today);
            if (row is null)
            {
                var title = header.TryGetValue("Title", out var t) && t < records[i].Length ? records[i][t] : string.Empty;
                errors.Add(new ImportRowError(lineNumber, Shorten(title), rowErrors));
                continue;
            }

            try
            {
                var outcome = await ImportRowAsync(db, userId, row, today, importedBookIds, cancellationToken);
                if (outcome is null)
                {
                    imported++;
                }
                else
                {
                    skipped.Add(new ImportSkippedRow(lineNumber, Shorten(row.Title), outcome));
                }
            }
            catch (Exception ex) when (ex is DomainException or AppException)
            {
                ClearTracking(db);
                errors.Add(new ImportRowError(lineNumber, Shorten(row.Title), [ex.Message]));
            }
            catch (DbUpdateException ex)
            {
                ClearTracking(db);
                logger.LogWarning(ex, "CSV import row {Line} failed to save", lineNumber);
                errors.Add(new ImportRowError(lineNumber, Shorten(row.Title), ["This row could not be saved."]));
            }
        }

        logger.LogInformation("CSV import finished: {Imported} imported, {Skipped} skipped, {Errors} invalid", imported, skipped.Count, errors.Count);
        return new ImportSummary(records.Count - 1, imported, skipped, errors);
    }

    /// <summary>Imports one validated row atomically. Returns null on success or a reason the row was skipped.</summary>
    private async Task<string?> ImportRowAsync(IReadNestDbContext db, Guid userId, LibraryImportRow row, DateOnly today, HashSet<Guid> importedBookIds, CancellationToken cancellationToken)
    {
        var book = await importer.ResolveManualAsync(
            db,
            userId,
            new ManualBookInput(row.Title, null, row.Authors, row.Isbn13, row.TotalPages, null, null, null, null, null, []),
            cancellationToken);

        if (importedBookIds.Contains(book.Id))
        {
            ClearTracking(db);
            return "Duplicate of an earlier row in this file.";
        }

        if (await db.UserBooks.AnyAsync(ub => ub.UserId == userId && ub.BookId == book.Id, cancellationToken))
        {
            ClearTracking(db);
            return "Already in your library.";
        }

        var now = clock.UtcNow;
        var entry = UserBook.Create(userId, book, ReadingStatus.WantToRead, today, now);
        if (row.TotalPages is not null)
        {
            entry.SetTotalPages(row.TotalPages, now);
        }

        if (row.Status != ReadingStatus.WantToRead)
        {
            entry.ChangeStatus(row.Status, today, row.Status == ReadingStatus.Read ? row.CompletedOn : null, now, defaultCompletionToToday: false);
        }

        if (row.StartedOn is not null)
        {
            entry.SetReadingDates(row.StartedOn, entry.CompletedOn, today, now);
        }

        if (row.CurrentPage is { } page && row.Status != ReadingStatus.Read)
        {
            entry.ValidatePage(page, "currentPage");
            entry.UpdateProgress(page, today, now);
        }

        entry.SetRating(row.Rating, now);
        entry.SetFavorite(row.IsFavorite, now);
        db.UserBooks.Add(entry);

        foreach (var shelfName in row.Shelves)
        {
            var shelf = await FindOrCreateShelfAsync(db, userId, shelfName, now, cancellationToken);
            if (shelf is not null)
            {
                db.CustomShelfBooks.Add(new CustomShelfBook { ShelfId = shelf.Id, Shelf = shelf, UserBookId = entry.Id, UserBook = entry, AddedAtUtc = now });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        ClearTracking(db);
        importedBookIds.Add(book.Id);
        return null;
    }

    private static async Task<CustomShelf?> FindOrCreateShelfAsync(IReadNestDbContext db, Guid userId, string name, DateTime now, CancellationToken cancellationToken)
    {
        var key = TextNormalizer.Key(name);
        if (key.Length == 0)
        {
            return null;
        }

        var shelf = db.CustomShelves.Local.FirstOrDefault(s => s.UserId == userId && s.NormalizedName == key)
            ?? await db.CustomShelves.FirstOrDefaultAsync(s => s.UserId == userId && s.NormalizedName == key, cancellationToken);
        if (shelf is not null)
        {
            return shelf;
        }

        if (await db.CustomShelves.CountAsync(s => s.UserId == userId, cancellationToken) >= Library.ShelfService.MaxShelvesPerUser)
        {
            return null;
        }

        shelf = CustomShelf.Create(userId, name, null, false, InputSanitizer.RandomToken(), now);
        db.CustomShelves.Add(shelf);
        return shelf;
    }

    private static async Task<string> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImportBytes)
            {
                throw new RequestValidationException("file", $"The file is larger than {MaxImportBytes / 1024} KB.");
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer.ToArray());
        }
        catch (DecoderFallbackException)
        {
            throw new RequestValidationException("file", "The file must be UTF-8 encoded text.");
        }
    }

    private static void ClearTracking(IReadNestDbContext db)
    {
        if (db is DbContext context)
        {
            context.ChangeTracker.Clear();
        }
    }

    private static string Shorten(string value) => value.Length <= 80 ? value : value[..80] + "…";
}
