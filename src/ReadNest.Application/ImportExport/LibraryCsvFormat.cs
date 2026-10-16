using System.Globalization;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.ImportExport;

/// <summary>A validated CSV import row.</summary>
public sealed record LibraryImportRow(
    int LineNumber,
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn13,
    ReadingStatus Status,
    int? CurrentPage,
    int? TotalPages,
    int? Rating,
    DateOnly? StartedOn,
    DateOnly? CompletedOn,
    bool IsFavorite,
    IReadOnlyList<string> Shelves);

/// <summary>
/// The documented library CSV format. Only <c>Title</c> is required; column order does not matter and header
/// names are case-insensitive. Multiple authors or shelves are separated with semicolons.
/// </summary>
public static class LibraryCsvFormat
{
    public static readonly IReadOnlyList<string> Columns =
        ["Title", "Author", "ISBN", "Status", "CurrentPage", "TotalPages", "Rating", "StartedOn", "CompletedOn", "Favorite", "Shelves"];

    public const string DateFormat = "yyyy-MM-dd";

    public static string StatusToCsv(ReadingStatus status) => status.ToDisplayName();

    public static bool TryParseStatus(string? value, out ReadingStatus status)
    {
        status = ReadingStatus.WantToRead;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var key = new string(value.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        switch (key)
        {
            case "WANTTOREAD" or "TOREAD" or "TBR":
                status = ReadingStatus.WantToRead;
                return true;
            case "CURRENTLYREADING" or "READING":
                status = ReadingStatus.CurrentlyReading;
                return true;
            case "READ" or "FINISHED" or "COMPLETED":
                status = ReadingStatus.Read;
                return true;
            case "PAUSED" or "ONHOLD":
                status = ReadingStatus.Paused;
                return true;
            case "DIDNOTFINISH" or "DNF" or "ABANDONED":
                status = ReadingStatus.DidNotFinish;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Validates one data row. Returns the row or a list of human-readable problems.</summary>
    public static (LibraryImportRow? Row, IReadOnlyList<string> Errors) ValidateRow(
        IReadOnlyDictionary<string, int> header, string[] record, int lineNumber, DateOnly today)
    {
        var errors = new List<string>();
        string? Get(string column)
        {
            if (!header.TryGetValue(column, out var index) || index >= record.Length)
            {
                return null;
            }

            var value = record[index].Trim();

            // Undo the formula-injection guard our own export adds, so exports re-import cleanly.
            if (value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@')
            {
                value = value[1..];
            }

            return value.Length == 0 ? null : value;
        }

        var title = Get("Title");
        if (title is null)
        {
            errors.Add("Title is required.");
        }
        else if (title.Length > Book.TitleMaxLength)
        {
            errors.Add($"Title must be at most {Book.TitleMaxLength} characters.");
        }

        var authors = SplitList(Get("Author"));
        if (authors.Any(a => a.Length > Author.NameMaxLength))
        {
            errors.Add($"Author names must be at most {Author.NameMaxLength} characters.");
        }

        string? isbn13 = null;
        if (Get("ISBN") is { } isbnRaw)
        {
            isbn13 = Isbn.NormalizeToIsbn13(isbnRaw);
            if (isbn13 is null)
            {
                errors.Add($"'{Truncate(isbnRaw)}' is not a valid ISBN-10 or ISBN-13.");
            }
        }

        if (!TryParseStatus(Get("Status"), out var status))
        {
            errors.Add($"Unknown status '{Truncate(Get("Status"))}'. Use Want to Read, Currently Reading, Read, Paused or Did Not Finish.");
        }

        var currentPage = ParseInt(Get("CurrentPage"), "CurrentPage", 0, Book.MaxPageCount, errors);
        var totalPages = ParseInt(Get("TotalPages"), "TotalPages", 1, Book.MaxPageCount, errors);
        var rating = ParseInt(Get("Rating"), "Rating", 1, 5, errors);
        if (currentPage is { } cp && totalPages is { } tp && cp > tp)
        {
            errors.Add("CurrentPage cannot be greater than TotalPages.");
        }

        var startedOn = ParseDate(Get("StartedOn"), "StartedOn", today, errors);
        var completedOn = ParseDate(Get("CompletedOn"), "CompletedOn", today, errors);
        if (completedOn is not null && status != ReadingStatus.Read)
        {
            errors.Add("CompletedOn can only be set when Status is Read.");
        }

        if (startedOn is { } s && completedOn is { } c && s > c)
        {
            errors.Add("StartedOn cannot be after CompletedOn.");
        }

        var favorite = false;
        if (Get("Favorite") is { } fav)
        {
            switch (fav.ToUpperInvariant())
            {
                case "TRUE" or "YES" or "Y" or "1":
                    favorite = true;
                    break;
                case "FALSE" or "NO" or "N" or "0":
                    break;
                default:
                    errors.Add("Favorite must be true or false.");
                    break;
            }
        }

        var shelves = SplitList(Get("Shelves"));
        if (shelves.Any(sh => sh.Length > CustomShelf.NameMaxLength) || shelves.Count > 20)
        {
            errors.Add($"Use at most 20 shelves of up to {CustomShelf.NameMaxLength} characters.");
        }

        if (errors.Count > 0 || title is null)
        {
            return (null, errors);
        }

        return (new LibraryImportRow(lineNumber, title, authors, isbn13, status, currentPage, totalPages, rating, startedOn, completedOn, favorite, shelves), errors);
    }

    public static IReadOnlyDictionary<string, int> ParseHeader(string[] header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            var known = Columns.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
                ?? (name.Equals("Authors", StringComparison.OrdinalIgnoreCase) ? "Author" : null)
                ?? (name.Equals("ISBN13", StringComparison.OrdinalIgnoreCase) || name.Equals("ISBN10", StringComparison.OrdinalIgnoreCase) ? "ISBN" : null);
            if (known is not null)
            {
                map.TryAdd(known, i);
            }
        }

        return map;
    }

    private static List<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static int? ParseInt(string? value, string column, int min, int max, List<string> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
        {
            errors.Add($"{column} must be a whole number between {min} and {max}.");
            return null;
        }

        return number;
    }

    private static DateOnly? ParseDate(string? value, string column, DateOnly today, List<string> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year < 1900)
        {
            errors.Add($"{column} must be a date in the format {DateFormat}.");
            return null;
        }

        if (date > today)
        {
            errors.Add($"{column} cannot be in the future.");
            return null;
        }

        return date;
    }

    private static string Truncate(string? value) => value is null ? string.Empty : value.Length <= 40 ? value : value[..40] + "…";
}
