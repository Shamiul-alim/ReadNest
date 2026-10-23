namespace ReadNest.Application.Abstractions;

/// <summary>What Google Books says about reading a volume (the volume's <c>accessInfo</c>).</summary>
/// <param name="Viewability">ALL_PAGES, PARTIAL, NO_PAGES or UNKNOWN.</param>
public sealed record GoogleAccessInfo(string VolumeId, string Viewability, bool Embeddable, bool PublicDomain, string? WebReaderLink);

/// <summary>Open Library's reading availability for a work.</summary>
/// <param name="EbookAccess">public, borrowable, printdisabled or no_ebook.</param>
public sealed record OpenLibraryAccess(string WorkId, string Title, string EbookAccess);

/// <summary>A Project Gutenberg book as described by the Gutendex catalogue API.</summary>
/// <param name="Copyright">False when Project Gutenberg marks the book as public domain in the USA; null when unknown.</param>
public sealed record GutenbergBook(int Id, string Title, IReadOnlyList<string> Authors, bool? Copyright, string? EpubUrl, IReadOnlyList<string> Languages);

/// <summary>
/// Looks up legitimate ways to read a book online. Implementations only use official APIs and links; they never
/// fetch restricted text.
/// </summary>
public interface IReadingSourceProvider
{
    /// <summary>Access information for a Google Books volume id, or the first volume matching an ISBN.</summary>
    Task<GoogleAccessInfo?> GetGoogleAccessAsync(string? volumeId, string? isbn, CancellationToken cancellationToken);

    Task<OpenLibraryAccess?> FindOpenLibraryAsync(string? workId, string? isbn, string title, string? author, CancellationToken cancellationToken);

    Task<IReadOnlyList<GutenbergBook>> SearchGutenbergAsync(string title, string? author, CancellationToken cancellationToken);

    Task<GutenbergBook?> GetGutenbergAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the EPUB download of a public-domain Gutenberg book. Only https URLs on gutenberg.org are followed,
    /// including redirects. The caller disposes the stream and enforces the size limit.
    /// </summary>
    Task<Stream> OpenGutenbergEpubAsync(GutenbergBook book, CancellationToken cancellationToken);
}

/// <summary>A reading source could not be reached (network failure, timeout or rate limit).</summary>
public sealed class ReadingSourceUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
