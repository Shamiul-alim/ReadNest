using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Notes;

public sealed record NoteRequest(NoteType Type, string Content, int? PageNumber, string? Chapter);

public sealed record NoteDto(
    Guid Id,
    Guid EntryId,
    Guid BookId,
    string BookTitle,
    NoteType Type,
    string Content,
    int? PageNumber,
    string? Chapter,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record NoteQuery(string? Search = null, NoteType? Type = null, Guid? EntryId = null, int Page = 1, int PageSize = 20);

/// <summary>
/// Private notes, quotes and takeaways. Notes are never exposed publicly; every query is scoped to the
/// owner of the library entry the note belongs to.
/// </summary>
public sealed class NoteService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public const int MaxNotesPerEntry = 2_000;

    public async Task<NoteDto> CreateAsync(Guid userId, Guid entryId, NoteRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LibraryService.LoadOwnedAsync(db, userId, entryId, cancellationToken);
        if (await db.ReadingNotes.CountAsync(n => n.UserBookId == entryId, cancellationToken) >= MaxNotesPerEntry)
        {
            throw new ConflictException($"A book can have at most {MaxNotesPerEntry} notes.");
        }

        var note = ReadingNote.Create(entry, request.Type, request.Content, request.PageNumber, request.Chapter, clock.UtcNow);
        db.ReadingNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(db, userId, note.Id, cancellationToken);
    }

    public async Task<NoteDto> UpdateAsync(Guid userId, Guid noteId, NoteRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var note = await db.ReadingNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("That note was not found.");
        note.Update(request.Type, request.Content, request.PageNumber, request.Chapter, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(db, userId, note.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid noteId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var note = await db.ReadingNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("That note was not found.");
        db.ReadingNotes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<NoteDto>> SearchAsync(Guid userId, NoteQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var notes = db.ReadingNotes.AsNoTracking().Where(n => n.UserBook.UserId == userId);
        if (query.EntryId is { } entryId)
        {
            notes = notes.Where(n => n.UserBookId == entryId);
        }

        if (query.Type is { } type)
        {
            notes = notes.Where(n => n.Type == type);
        }

        if (InputSanitizer.SearchTerm(query.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            notes = notes.Where(n =>
                n.Content.ToLower().Contains(term)
                || (n.Chapter != null && n.Chapter.ToLower().Contains(term))
                || n.UserBook.Book.Title.ToLower().Contains(term));
        }

        return await notes
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(Projection)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<NamedEntryDto>> GetBooksWithNotesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.Notes.Any())
            .OrderBy(ub => ub.Book.Title)
            .Select(ub => new NamedEntryDto(ub.Id, ub.Book.Title, ub.Notes.Count))
            .ToListAsync(cancellationToken);
    }

    private static async Task<NoteDto> GetAsync(IReadNestDbContext db, Guid userId, Guid noteId, CancellationToken cancellationToken) =>
        await db.ReadingNotes.AsNoTracking()
            .Where(n => n.Id == noteId && n.UserBook.UserId == userId)
            .Select(Projection)
            .FirstAsync(cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<ReadingNote, NoteDto>> Projection = n => new NoteDto(
        n.Id,
        n.UserBookId,
        n.UserBook.BookId,
        n.UserBook.Book.Title,
        n.Type,
        n.Content,
        n.PageNumber,
        n.Chapter,
        n.CreatedAtUtc,
        n.UpdatedAtUtc);
}

public sealed record NamedEntryDto(Guid EntryId, string Title, int Count);
