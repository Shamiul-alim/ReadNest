using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Domain.Entities;

namespace ReadNest.Application.Reading;

public sealed record SessionRequest(DateOnly? ReadOn, int StartPage, int EndPage, int DurationMinutes, string? Reflection);

public sealed record ReadingSessionDto(
    Guid Id,
    Guid EntryId,
    Guid BookId,
    string BookTitle,
    string? CoverUrl,
    DateOnly ReadOn,
    int StartPage,
    int EndPage,
    int PagesRead,
    int DurationMinutes,
    string? Reflection,
    DateTime CreatedAtUtc);

public sealed record HistoryQuery(DateOnly? From = null, DateOnly? To = null, Guid? EntryId = null, int Page = 1, int PageSize = 50);

/// <summary>
/// Reading sessions. Recording or editing a session moves the library bookmark forward when the session
/// ends beyond it. Editing or deleting a session never moves the bookmark backwards, because the reader
/// may have progressed without logging every sitting.
/// </summary>
public sealed class ReadingSessionService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public async Task<ReadingSessionDto> LogAsync(Guid userId, Guid entryId, SessionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LibraryService.LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var readOn = request.ReadOn ?? today;

        var duplicate = await db.ReadingSessions.AnyAsync(
            s => s.UserBookId == entryId && s.ReadOn == readOn && s.StartPage == request.StartPage && s.EndPage == request.EndPage,
            cancellationToken);
        if (duplicate)
        {
            throw new ConflictException("This session has already been recorded.");
        }

        var session = ReadingSession.Create(entry, readOn, request.StartPage, request.EndPage, request.DurationMinutes, request.Reflection, today, clock.UtcNow);
        db.ReadingSessions.Add(session);
        await SaveAsync(db, cancellationToken);
        return await GetAsync(db, userId, session.Id, cancellationToken);
    }

    public async Task<ReadingSessionDto> UpdateAsync(Guid userId, Guid sessionId, SessionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var session = await db.ReadingSessions
            .Include(s => s.UserBook)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("That reading session was not found.");
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        session.Update(request.ReadOn ?? session.ReadOn, request.StartPage, request.EndPage, request.DurationMinutes, request.Reflection, today, clock.UtcNow);
        await SaveAsync(db, cancellationToken);
        return await GetAsync(db, userId, session.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var session = await db.ReadingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserBook.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("That reading session was not found.");
        db.ReadingSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<ReadingSessionDto>> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var sessions = db.ReadingSessions.AsNoTracking().Where(s => s.UserBook.UserId == userId);
        if (query.From is { } from)
        {
            sessions = sessions.Where(s => s.ReadOn >= from);
        }

        if (query.To is { } to)
        {
            sessions = sessions.Where(s => s.ReadOn <= to);
        }

        if (query.EntryId is { } entryId)
        {
            sessions = sessions.Where(s => s.UserBookId == entryId);
        }

        return await sessions
            .OrderByDescending(s => s.ReadOn)
            .ThenByDescending(s => s.CreatedAtUtc)
            .Select(Projection)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    private static async Task SaveAsync(IReadNestDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            throw new ConflictException("An identical session already exists for that day and page range.");
        }
    }

    private static async Task<ReadingSessionDto> GetAsync(IReadNestDbContext db, Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        await db.ReadingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserBook.UserId == userId)
            .Select(Projection)
            .FirstAsync(cancellationToken);

    internal static readonly System.Linq.Expressions.Expression<Func<ReadingSession, ReadingSessionDto>> Projection = s => new ReadingSessionDto(
        s.Id,
        s.UserBookId,
        s.UserBook.BookId,
        s.UserBook.Book.Title,
        s.UserBook.Book.CoverImageUrl,
        s.ReadOn,
        s.StartPage,
        s.EndPage,
        s.EndPage - s.StartPage,
        s.DurationMinutes,
        s.Reflection,
        s.CreatedAtUtc);
}
