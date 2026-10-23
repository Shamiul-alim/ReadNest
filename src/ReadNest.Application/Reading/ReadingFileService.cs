using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.Application.Reading;

public sealed record ReaderPositionDto(string? Location, decimal? Percent, int? Page, int? PageCount, DateTime? UpdatedAtUtc);

public sealed record ReadingFileDto(
    Guid EntryId,
    ReadingFileFormat Format,
    ReadingFileSource Source,
    string FileName,
    long SizeBytes,
    DateTime UploadedAtUtc,
    string? SourceUrl,
    string? SourceNote,
    ReaderPositionDto Position);

public sealed record ReaderPositionRequest(string? Location, decimal? Percent, int? Page, int? PageCount);

/// <summary>Opened file content. The caller owns (and must dispose) the stream.</summary>
public sealed record ReadingFileContent(Stream Content, ReadingFileFormat Format, string FileName, long Length, string Sha256, DateTime UploadedAtUtc);

/// <summary>A file to attach from another source (for example a public-domain EPUB from Project Gutenberg).</summary>
public sealed record ImportedReadingFile(Stream Content, string FileName, ReadingFileSource Source, string? SourceUrl, string? SourceNote);

/// <summary>
/// Private PDF/EPUB files attached to library entries. Every method is scoped to the authenticated user: a file
/// belonging to someone else's entry behaves exactly like a missing one.
/// </summary>
public sealed class ReadingFileService(
    IReadNestDbContextFactory dbFactory,
    IFileStore files,
    IOptions<UploadOptions> uploadOptions,
    UserClock clock,
    ILogger<ReadingFileService> logger)
{
    public const string Container = "books";

    public long MaxBytes => uploadOptions.Value.ReadingFileMaxBytes;

    public async Task<ReadingFileDto?> GetAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        await EnsureOwnedEntryAsync(db, userId, entryId, cancellationToken);
        return await db.ReadingFiles.AsNoTracking()
            .Where(f => f.UserBookId == entryId)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Entry ids (of this user's library) that have a reading file attached.</summary>
    public async Task<HashSet<Guid>> GetEntriesWithFilesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var ids = await db.ReadingFiles.AsNoTracking()
            .Where(f => f.UserBook.UserId == userId)
            .Select(f => f.UserBookId)
            .ToListAsync(cancellationToken);
        return [.. ids];
    }

    /// <summary>
    /// Validates and stores an uploaded PDF or EPUB, then attaches it to the entry. A file already attached is
    /// only deleted after the new one is safely stored and recorded.
    /// </summary>
    public async Task<ReadingFileDto> UploadAsync(Guid userId, Guid entryId, Stream content, string? fileName, CancellationToken cancellationToken)
    {
        var declared = ReadingFileInspector.FormatFromName(fileName)
            ?? throw new RequestValidationException("file", "Choose a .pdf or .epub file.");
        return await AttachAsync(userId, entryId, content, fileName, declared, ReadingFileSource.Upload, null, null, cancellationToken);
    }

    internal async Task<ReadingFileDto> ImportAsync(Guid userId, Guid entryId, ImportedReadingFile file, CancellationToken cancellationToken) =>
        await AttachAsync(userId, entryId, file.Content, file.FileName, null, file.Source, file.SourceUrl, file.SourceNote, cancellationToken);

    public async Task RemoveAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var file = await db.ReadingFiles.FirstOrDefaultAsync(f => f.UserBookId == entryId && f.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("This book has no file attached.");
        var key = file.StorageKey;
        db.ReadingFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(key, CancellationToken.None);
    }

    /// <summary>Opens the file for the owner. Missing rows, other users' entries and missing files are all "not found".</summary>
    public async Task<ReadingFileContent> OpenAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var file = await db.ReadingFiles.AsNoTracking()
            .Where(f => f.UserBookId == entryId && f.UserBook.UserId == userId)
            .Select(f => new { f.StorageKey, f.Format, f.FileName, f.SizeBytes, f.Sha256, f.UploadedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("This book has no file attached.");

        var stream = files.OpenRead(file.StorageKey);
        if (stream is null)
        {
            logger.LogWarning("Reading file for entry {EntryId} is missing from storage.", entryId);
            throw new NotFoundException("The file for this book is missing. Upload it again to keep reading.");
        }

        return new ReadingFileContent(stream, file.Format, file.FileName, stream.Length, file.Sha256, file.UploadedAtUtc);
    }

    /// <summary>
    /// Saves the reader's position in the file. This only moves the file bookmark; reading sessions and the
    /// entry's page progress are recorded separately (see <see cref="ReaderProgress"/>).
    /// </summary>
    public async Task<ReaderPositionDto> SavePositionAsync(Guid userId, Guid entryId, ReaderPositionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var file = await db.ReadingFiles.FirstOrDefaultAsync(f => f.UserBookId == entryId && f.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("This book has no file attached.");
        try
        {
            file.UpdatePosition(request.Location, request.Percent, request.Page, request.PageCount, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw RequestValidationException.From(ex);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReaderPositionDto(file.LastLocation, file.LastPercent, file.LastPage, file.PageCount, file.PositionUpdatedAtUtc);
    }

    public async Task MarkOpenedAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var file = await db.ReadingFiles.FirstOrDefaultAsync(f => f.UserBookId == entryId && f.UserBook.UserId == userId, cancellationToken);
        if (file is not null)
        {
            file.MarkOpened(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Storage keys of every file a user owns (reading files and profile photo), for account deletion.</summary>
    public async Task<IReadOnlyList<string>> GetStoredKeysAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var keys = await db.ReadingFiles.AsNoTracking().Where(f => f.UserBook.UserId == userId).Select(f => f.StorageKey).ToListAsync(cancellationToken);
        var photo = await db.UserProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.PhotoKey).FirstOrDefaultAsync(cancellationToken);
        if (photo is not null)
        {
            keys.Add(photo);
        }

        return keys;
    }

    public async Task DeleteStoredFilesAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            await files.DeleteAsync(key, CancellationToken.None);
        }
    }

    private async Task<ReadingFileDto> AttachAsync(
        Guid userId,
        Guid entryId,
        Stream content,
        string? fileName,
        ReadingFileFormat? declared,
        ReadingFileSource source,
        string? sourceUrl,
        string? sourceNote,
        CancellationToken cancellationToken)
    {
        // Check ownership before accepting any bytes.
        await using (var check = await dbFactory.CreateAsync(cancellationToken))
        {
            await EnsureOwnedEntryAsync(check, userId, entryId, cancellationToken);
        }

        IStagedFile staged;
        try
        {
            staged = await files.StageAsync(content, MaxBytes, cancellationToken);
        }
        catch (FileTooLargeException)
        {
            throw new RequestValidationException("file", $"The file is too large. The limit is {UploadOptions.Describe(MaxBytes)}.");
        }

        await using (staged)
        {
            if (staged.Length == 0)
            {
                throw new RequestValidationException("file", "The file is empty.");
            }

            ReadingFileFormat format;
            await using (var inspect = staged.OpenRead())
            {
                try
                {
                    format = ReadingFileInspector.Inspect(inspect);
                }
                catch (InvalidDataException ex)
                {
                    throw new RequestValidationException("file", ex.Message);
                }
            }

            if (declared is { } expected && expected != format)
            {
                throw new RequestValidationException("file", $"This file's contents don't match its name: it is not a {expected.ToDisplayName()} file.");
            }

            var key = await staged.CommitAsync(Container, ReadingFileInspector.ExtensionFor(format), cancellationToken);
            string? replacedKey = null;
            try
            {
                await using var db = await dbFactory.CreateAsync(cancellationToken);
                var entry = await db.UserBooks.FirstOrDefaultAsync(ub => ub.Id == entryId && ub.UserId == userId, cancellationToken)
                    ?? throw new NotFoundException("That library entry was not found.");
                var existing = await db.ReadingFiles.FirstOrDefaultAsync(f => f.UserBookId == entryId, cancellationToken);
                var name = ReadingFileInspector.DisplayName(fileName, format);
                var now = clock.UtcNow;
                if (existing is null)
                {
                    db.ReadingFiles.Add(ReadingFile.Create(entry, format, source, key, name, staged.Length, staged.Sha256, sourceUrl, sourceNote, now));
                }
                else
                {
                    replacedKey = existing.Replace(format, source, key, name, staged.Length, staged.Sha256, sourceUrl, sourceNote, now);
                }

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
                {
                    throw new ConflictException("Another upload for this book finished at the same time. Refresh and try again.");
                }
            }
            catch
            {
                // The database still points at the previous file (if any); discard the new one.
                await files.DeleteAsync(key, CancellationToken.None);
                throw;
            }

            if (replacedKey is not null && replacedKey != key)
            {
                await files.DeleteAsync(replacedKey, CancellationToken.None);
            }
        }

        return await GetAsync(userId, entryId, cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");
    }

    private static async Task EnsureOwnedEntryAsync(IReadNestDbContext db, Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        if (!await db.UserBooks.AnyAsync(ub => ub.Id == entryId && ub.UserId == userId, cancellationToken))
        {
            throw new NotFoundException("That library entry was not found.");
        }
    }

    private static readonly System.Linq.Expressions.Expression<Func<ReadingFile, ReadingFileDto>> Projection = f => new ReadingFileDto(
        f.UserBookId,
        f.Format,
        f.Source,
        f.FileName,
        f.SizeBytes,
        f.UploadedAtUtc,
        f.SourceUrl,
        f.SourceNote,
        new ReaderPositionDto(f.LastLocation, f.LastPercent, f.LastPage, f.PageCount, f.PositionUpdatedAtUtc));
}
