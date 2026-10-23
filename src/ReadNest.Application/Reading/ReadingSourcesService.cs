using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Reading;

/// <summary>How much of a book a reading option gives access to.</summary>
public enum ReadingAccess
{
    /// <summary>The whole book can be read (your own file, or a public-domain edition).</summary>
    FullText = 1,

    /// <summary>Only part of the book is viewable (a publisher preview).</summary>
    Preview = 2,

    /// <summary>The book can be borrowed from a lending library, on that library's site.</summary>
    Borrow = 3,

    /// <summary>A page about the book on another site; ReadNest cannot tell what is readable there.</summary>
    External = 4,
}

public enum ReadingProvider
{
    YourFile = 1,
    GoogleBooks = 2,
    OpenLibrary = 3,
    ProjectGutenberg = 4,
}

/// <param name="ReaderPath">A ReadNest page that shows the book (relative), when it can be read in the app.</param>
/// <param name="ExternalUrl">An official page on the provider's own site.</param>
/// <param name="GutenbergId">Set when a public-domain EPUB can be added to the reader's library entry.</param>
public sealed record ReadingOptionDto(
    ReadingProvider Provider,
    ReadingAccess Access,
    string Label,
    string Description,
    string? ReaderPath,
    string? ExternalUrl,
    int? GutenbergId = null,
    string? Notice = null);

/// <summary>Result of checking one provider. <see cref="Unavailable"/> means the provider could not be reached.</summary>
public sealed record ReadingSourceResult(ReadingProvider Provider, IReadOnlyList<ReadingOptionDto> Options, bool Unavailable = false, string? FallbackUrl = null);

/// <summary>The book being looked up, resolved from the catalogue.</summary>
public sealed record ReadingSourceBook(Guid BookId, string Title, string? Author, string? Isbn13, string? GoogleVolumeId, string? OpenLibraryWorkId);

/// <summary>
/// Finds legitimate ways to read a catalogue book. Results come only from official APIs; nothing here implies
/// that a book is readable unless the provider says so. Lookups are cached because the providers are slow
/// (Gutendex can take a minute) and rate limited.
/// </summary>
public sealed class ReadingSourcesService(
    IReadNestDbContextFactory dbFactory,
    IReadingSourceProvider provider,
    ReadingFileService readingFiles,
    IMemoryCache cache,
    ILogger<ReadingSourcesService> logger)
{
    public const string GutenbergNotice =
        "Project Gutenberg lists this book as public domain in the United States. Copyright laws differ between countries, so check the law where you live before reading or sharing it.";

    private static readonly TimeSpan HitDuration = TimeSpan.FromHours(12);
    private static readonly TimeSpan FailureDuration = TimeSpan.FromMinutes(5);

    public async Task<ReadingSourceBook> GetBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var book = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new
            {
                b.Id,
                b.Title,
                Author = b.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).FirstOrDefault(),
                b.Isbn13,
                Google = b.ExternalIds.Where(e => e.Provider == CatalogProvider.GoogleBooks).Select(e => e.ExternalId).FirstOrDefault(),
                OpenLibrary = b.ExternalIds.Where(e => e.Provider == CatalogProvider.OpenLibrary).Select(e => e.ExternalId).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That book was not found.");
        return new ReadingSourceBook(book.Id, book.Title, book.Author, book.Isbn13, book.Google, book.OpenLibrary);
    }

    /// <summary>All providers, checked in parallel (used by the API).</summary>
    public async Task<IReadOnlyList<ReadingSourceResult>> GetAllAsync(Guid? userId, Guid bookId, CancellationToken cancellationToken)
    {
        var book = await GetBookAsync(bookId, cancellationToken);
        var results = new List<ReadingSourceResult>();
        if (userId is { } id)
        {
            results.Add(await GetYourFileAsync(id, bookId, cancellationToken));
        }

        results.AddRange(await Task.WhenAll(
            GetGoogleAsync(book, cancellationToken),
            GetOpenLibraryAsync(book, cancellationToken),
            GetGutenbergAsync(book, cancellationToken)));
        return results;
    }

    public async Task<ReadingSourceResult> GetYourFileAsync(Guid userId, Guid bookId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var file = await db.ReadingFiles.AsNoTracking()
            .Where(f => f.UserBook.UserId == userId && f.UserBook.BookId == bookId)
            .Select(f => new { f.UserBookId, f.Format, f.Source, f.FileName })
            .FirstOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return new ReadingSourceResult(ReadingProvider.YourFile, []);
        }

        var label = file.Source == ReadingFileSource.ProjectGutenberg ? "Read the Project Gutenberg edition" : "Read your " + file.Format.ToDisplayName();
        return new ReadingSourceResult(ReadingProvider.YourFile,
        [
            new ReadingOptionDto(ReadingProvider.YourFile, ReadingAccess.FullText, label, file.FileName, $"reader/{file.UserBookId}", null),
        ]);
    }

    public Task<ReadingSourceResult> GetGoogleAsync(ReadingSourceBook book, CancellationToken cancellationToken)
    {
        if (book.GoogleVolumeId is null && book.Isbn13 is null)
        {
            return Task.FromResult(new ReadingSourceResult(ReadingProvider.GoogleBooks, []));
        }

        return CachedAsync(
            $"read-src:google:{book.GoogleVolumeId}:{book.Isbn13}",
            ReadingProvider.GoogleBooks,
            async ct =>
            {
                var access = await provider.GetGoogleAccessAsync(book.GoogleVolumeId, book.Isbn13, ct);
                return access is null ? [] : GoogleOptions(access);
            },
            fallbackUrl: book.GoogleVolumeId is null ? null : $"https://books.google.com/books?id={Uri.EscapeDataString(book.GoogleVolumeId)}",
            cancellationToken);
    }

    public Task<ReadingSourceResult> GetOpenLibraryAsync(ReadingSourceBook book, CancellationToken cancellationToken) =>
        CachedAsync(
            $"read-src:ol:{book.OpenLibraryWorkId}:{book.Isbn13}:{TextNormalizer.Key(book.Title)}",
            ReadingProvider.OpenLibrary,
            async ct =>
            {
                var access = await provider.FindOpenLibraryAsync(book.OpenLibraryWorkId, book.Isbn13, book.Title, book.Author, ct);
                return access is null ? [] : OpenLibraryOptions(access);
            },
            fallbackUrl: "https://openlibrary.org/search?q=" + Uri.EscapeDataString(book.Title + " " + book.Author),
            cancellationToken);

    public Task<ReadingSourceResult> GetGutenbergAsync(ReadingSourceBook book, CancellationToken cancellationToken) =>
        CachedAsync(
            $"read-src:pg:{TextNormalizer.Key(book.Title)}:{TextNormalizer.Key(book.Author ?? string.Empty)}",
            ReadingProvider.ProjectGutenberg,
            async ct =>
            {
                var matches = await provider.SearchGutenbergAsync(book.Title, book.Author, ct);
                var match = BestGutenbergMatch(matches, book.Title, book.Author);
                if (match is null)
                {
                    return [];
                }

                cache.Set(GutenbergKey(match.Id), match, HitDuration);
                return [GutenbergOption(match)];
            },
            fallbackUrl: "https://www.gutenberg.org/ebooks/search/?query=" + Uri.EscapeDataString(book.Title + " " + book.Author),
            cancellationToken);

    /// <summary>
    /// Downloads a public-domain EPUB from Project Gutenberg and attaches it to the reader's library entry, with
    /// attribution. Refused unless Project Gutenberg marks the book as not under copyright.
    /// </summary>
    public async Task<ReadingFileDto> ImportGutenbergAsync(Guid userId, Guid entryId, int gutenbergId, CancellationToken cancellationToken)
    {
        // Ownership first: nothing is downloaded for an entry the user does not own.
        if (await readingFiles.GetAsync(userId, entryId, cancellationToken) is { } existing && existing.Source == ReadingFileSource.Upload)
        {
            throw new ConflictException("This book already has your own file attached. Remove it first to use the Project Gutenberg edition.");
        }

        if (!cache.TryGetValue(GutenbergKey(gutenbergId), out GutenbergBook? book) || book is null)
        {
            try
            {
                book = await provider.GetGutenbergAsync(gutenbergId, cancellationToken);
            }
            catch (ReadingSourceUnavailableException)
            {
                throw new ServiceUnavailableException("Project Gutenberg can't be reached right now. Try again in a few minutes.");
            }
        }

        if (book is null)
        {
            throw new NotFoundException("That Project Gutenberg book was not found.");
        }

        if (book.Copyright != false || book.EpubUrl is null)
        {
            throw new ConflictException("Project Gutenberg doesn't list this book as a public-domain EPUB, so it can't be added.");
        }

        Stream download;
        try
        {
            download = await provider.OpenGutenbergEpubAsync(book, cancellationToken);
        }
        catch (ReadingSourceUnavailableException)
        {
            throw new ServiceUnavailableException("The download from Project Gutenberg failed. Try again in a few minutes.");
        }

        await using (download)
        {
            var note = $"Project Gutenberg eBook #{book.Id.ToString(CultureInfo.InvariantCulture)}. {GutenbergNotice}";
            return await readingFiles.ImportAsync(
                userId,
                entryId,
                new ImportedReadingFile(download, SafeName(book.Title) + ".epub", ReadingFileSource.ProjectGutenberg, GutenbergPage(book.Id), note),
                cancellationToken);
        }
    }

    internal static IReadOnlyList<ReadingOptionDto> GoogleOptions(GoogleAccessInfo access)
    {
        var page = access.WebReaderLink ?? $"https://books.google.com/books?id={Uri.EscapeDataString(access.VolumeId)}";
        var readerPath = $"reader/google/{Uri.EscapeDataString(access.VolumeId)}";
        return access.Viewability switch
        {
            "ALL_PAGES" when access.Embeddable => [new ReadingOptionDto(ReadingProvider.GoogleBooks, ReadingAccess.FullText,
                "Read on Google Books", access.PublicDomain ? "Full view: public domain." : "Full view provided by Google Books.", readerPath, page)],
            "PARTIAL" when access.Embeddable => [new ReadingOptionDto(ReadingProvider.GoogleBooks, ReadingAccess.Preview,
                "Read Preview", "A preview chosen by the publisher; some pages are not shown.", readerPath, page)],
            "ALL_PAGES" or "PARTIAL" => [new ReadingOptionDto(ReadingProvider.GoogleBooks, access.Viewability == "ALL_PAGES" ? ReadingAccess.FullText : ReadingAccess.Preview,
                "Open on Google Books", "Google Books shows this book on its own site only.", null, page)],
            _ => [],
        };
    }

    internal static IReadOnlyList<ReadingOptionDto> OpenLibraryOptions(OpenLibraryAccess access)
    {
        var page = $"https://openlibrary.org/works/{Uri.EscapeDataString(access.WorkId)}";
        return access.EbookAccess switch
        {
            "public" => [new ReadingOptionDto(ReadingProvider.OpenLibrary, ReadingAccess.FullText,
                "Read free on Open Library", "A public scan you can read on Open Library.", null, page)],
            "borrowable" => [new ReadingOptionDto(ReadingProvider.OpenLibrary, ReadingAccess.Borrow,
                "Borrow on Open Library", "Free with an Open Library account, subject to availability.", null, page,
                Notice: "You'll leave ReadNest. Borrowing happens on Open Library under its own terms.")],
            _ => [],
        };
    }

    internal static ReadingOptionDto GutenbergOption(GutenbergBook book) =>
        book.Copyright == false && book.EpubUrl is not null
            ? new ReadingOptionDto(ReadingProvider.ProjectGutenberg, ReadingAccess.FullText,
                "Read free from Project Gutenberg", "Public-domain edition (EPUB). Add it to read here, with your place saved.",
                null, GutenbergPage(book.Id), book.Id, GutenbergNotice)
            : new ReadingOptionDto(ReadingProvider.ProjectGutenberg, ReadingAccess.External,
                "View on Project Gutenberg", "Copyright status isn't confirmed, so it can only be opened on Project Gutenberg.",
                null, GutenbergPage(book.Id));

    /// <summary>
    /// Picks the Gutenberg book that is the same work: the normalised titles must match (ignoring a subtitle) and,
    /// when the author is known, the author's surname must appear among the book's authors.
    /// </summary>
    internal static GutenbergBook? BestGutenbergMatch(IReadOnlyList<GutenbergBook> books, string title, string? author)
    {
        var wanted = TitleKey(title);
        var surname = author is null ? null : TextNormalizer.Key(author.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? author);
        return books
            .Where(b => TitleKey(b.Title) == wanted)
            .Where(b => surname is null || b.Authors.Any(a => TextNormalizer.Key(a).Contains(surname, StringComparison.Ordinal)))
            .OrderBy(b => b.Copyright == false ? 0 : 1)
            .ThenBy(b => b.EpubUrl is null ? 1 : 0)
            .ThenBy(b => b.Id)
            .FirstOrDefault();
    }

    private static string TitleKey(string title) => TextNormalizer.Key(title.Split([':', ';', '('], 2)[0]);

    private static string GutenbergPage(int id) => $"https://www.gutenberg.org/ebooks/{id.ToString(CultureInfo.InvariantCulture)}";

    private static string GutenbergKey(int id) => $"read-src:pg-book:{id.ToString(CultureInfo.InvariantCulture)}";

    private static string SafeName(string title)
    {
        var cleaned = new string(title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return cleaned.Length == 0 ? "book" : cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    private async Task<ReadingSourceResult> CachedAsync(
        string key,
        ReadingProvider source,
        Func<CancellationToken, Task<IReadOnlyList<ReadingOptionDto>>> lookup,
        string? fallbackUrl,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out ReadingSourceResult? cached) && cached is not null)
        {
            return cached;
        }

        ReadingSourceResult result;
        try
        {
            result = new ReadingSourceResult(source, await lookup(cancellationToken));
            cache.Set(key, result, HitDuration);
        }
        catch (ReadingSourceUnavailableException ex)
        {
            logger.LogInformation("Reading source {Source} unavailable: {Reason}", source, ex.Message);
            result = new ReadingSourceResult(source, [], Unavailable: true, FallbackUrl: fallbackUrl);
            cache.Set(key, result, FailureDuration);
        }

        return result;
    }
}
