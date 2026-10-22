using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Library;

public sealed record OwnershipRequest(OwnershipStatus Ownership, BookFormat? Format, DateOnly? AcquiredOn, decimal? PurchasePrice, string? ConditionNotes);

public sealed record LoanRequest(string BorrowerName, DateOnly? LoanedOn, DateOnly? DueOn, string? Notes);

public enum LoanFilter
{
    All = 0,
    OnLoan = 1,
    Available = 2,
}

public sealed record CollectionQuery(string? Search = null, BookFormat? Format = null, LoanFilter Loans = LoanFilter.All, int Page = 1, int PageSize = 24, OwnershipStatus Ownership = OwnershipStatus.Owned);

public sealed record LoanDto(
    Guid Id,
    Guid EntryId,
    Guid BookId,
    string BookTitle,
    string BorrowerName,
    DateOnly LoanedOn,
    DateOnly? DueOn,
    DateOnly? ReturnedOn,
    string? Notes,
    bool IsOverdue,
    string? CoverUrl = null);

public sealed record CollectionItemDto(
    Guid EntryId,
    Guid BookId,
    string Title,
    IReadOnlyList<string> Authors,
    string? CoverUrl,
    BookFormat? Format,
    DateOnly? AcquiredOn,
    decimal? PurchasePrice,
    string? ConditionNotes,
    ReadingStatus Status,
    LoanDto? OutstandingLoan);

public sealed record CollectionSummaryDto(int OwnedBooks, int Physical, int EBooks, int Audiobooks, int OnLoan, int Overdue, decimal TotalSpent, int BorrowedFromOthers);

/// <summary>
/// Owned books and lending. Loans are history: they are kept when returned and only disappear when the user
/// explicitly deletes a loan record or removes the book from the library (which is refused while a loan is outstanding).
/// </summary>
public sealed class CollectionService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public async Task UpdateOwnershipAsync(Guid userId, Guid entryId, OwnershipRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await db.UserBooks.Include(ub => ub.Loans)
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.Id == entryId, cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        entry.SetOwnership(request.Ownership, request.Format, request.AcquiredOn, request.PurchasePrice, request.ConditionNotes, today, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CollectionSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var owned = db.UserBooks.Where(ub => ub.UserId == userId && ub.Ownership == OwnershipStatus.Owned);
        var byFormat = await owned.GroupBy(ub => ub.Format).Select(g => new { Format = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var spent = await owned.SumAsync(ub => ub.PurchasePrice ?? 0, cancellationToken);
        var loans = db.BookLoans.Where(l => l.UserBook.UserId == userId && l.ReturnedOn == null);
        var onLoan = await loans.CountAsync(cancellationToken);
        var overdue = await loans.CountAsync(l => l.DueOn < today, cancellationToken);
        var borrowed = await db.UserBooks.CountAsync(ub => ub.UserId == userId && ub.Ownership == OwnershipStatus.Borrowed, cancellationToken);
        int Count(BookFormat? format) => byFormat.Where(x => x.Format == format).Sum(x => x.Count);
        return new CollectionSummaryDto(
            byFormat.Sum(x => x.Count),
            Count(BookFormat.Physical) + Count(null),
            Count(BookFormat.EBook),
            Count(BookFormat.Audiobook),
            onLoan,
            overdue,
            spent,
            borrowed);
    }

    public async Task<PagedResult<CollectionItemDto>> GetCollectionAsync(Guid userId, CollectionQuery query, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var ownership = query.Ownership == OwnershipStatus.Borrowed ? OwnershipStatus.Borrowed : OwnershipStatus.Owned;
        var items = db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId && ub.Ownership == ownership);
        if (query.Format is { } format)
        {
            items = format == BookFormat.Physical
                ? items.Where(ub => ub.Format == BookFormat.Physical || ub.Format == null)
                : items.Where(ub => ub.Format == format);
        }

        items = query.Loans switch
        {
            LoanFilter.OnLoan => items.Where(ub => ub.Loans.Any(l => l.ReturnedOn == null)),
            LoanFilter.Available => items.Where(ub => !ub.Loans.Any(l => l.ReturnedOn == null)),
            _ => items,
        };

        if (InputSanitizer.SearchTerm(query.Search) is { } search)
        {
            var key = TextNormalizer.Key(search);
            items = items.Where(ub => ub.Book.NormalizedTitle.Contains(key) || ub.Book.Authors.Any(a => a.Author.NormalizedName.Contains(key)));
        }

        return await items
            .OrderBy(ub => ub.Book.NormalizedTitle)
            .Select(ub => new CollectionItemDto(
                ub.Id,
                ub.BookId,
                ub.Book.Title,
                ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                ub.Book.CoverImageUrl,
                ub.Format,
                ub.AcquiredOn,
                ub.PurchasePrice,
                ub.ConditionNotes,
                ub.Status,
                ub.Loans.Where(l => l.ReturnedOn == null)
                    .Select(l => new LoanDto(l.Id, ub.Id, ub.BookId, ub.Book.Title, l.BorrowerName, l.LoanedOn, l.DueOn, l.ReturnedOn, l.Notes, l.DueOn < today, ub.Book.CoverImageUrl))
                    .FirstOrDefault()))
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<LoanDto>> GetLoansAsync(Guid userId, bool outstandingOnly, Guid? entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var loans = db.BookLoans.AsNoTracking().Where(l => l.UserBook.UserId == userId);
        if (outstandingOnly)
        {
            loans = loans.Where(l => l.ReturnedOn == null);
        }

        if (entryId is { } id)
        {
            loans = loans.Where(l => l.UserBookId == id);
        }

        return await loans
            .OrderBy(l => l.ReturnedOn != null)
            .ThenByDescending(l => l.LoanedOn)
            .Take(500)
            .Select(l => new LoanDto(l.Id, l.UserBookId, l.UserBook.BookId, l.UserBook.Book.Title, l.BorrowerName, l.LoanedOn, l.DueOn, l.ReturnedOn, l.Notes, l.ReturnedOn == null && l.DueOn < today, l.UserBook.Book.CoverImageUrl))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> LendAsync(Guid userId, Guid entryId, LoanRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await db.UserBooks.Include(ub => ub.Loans)
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.Id == entryId, cancellationToken)
            ?? throw new NotFoundException("That library entry was not found.");
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var loan = BookLoan.Create(entry, request.BorrowerName, request.LoanedOn ?? today, request.DueOn, request.Notes, today, clock.UtcNow);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            throw new ConflictException("This book is already lent out.");
        }

        return loan.Id;
    }

    public async Task ReturnAsync(Guid userId, Guid loanId, DateOnly? returnedOn, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var loan = await LoadLoanAsync(db, userId, loanId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        loan.MarkReturned(returnedOn ?? today, today, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLoanAsync(Guid userId, Guid loanId, LoanRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var loan = await LoadLoanAsync(db, userId, loanId, cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        loan.Update(request.BorrowerName, request.LoanedOn ?? loan.LoanedOn, request.DueOn, request.Notes, today, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Explicitly deletes a loan record (for correcting mistakes).</summary>
    public async Task DeleteLoanAsync(Guid userId, Guid loanId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var loan = await LoadLoanAsync(db, userId, loanId, cancellationToken);
        db.BookLoans.Remove(loan);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<BookLoan> LoadLoanAsync(IReadNestDbContext db, Guid userId, Guid loanId, CancellationToken cancellationToken) =>
        await db.BookLoans.FirstOrDefaultAsync(l => l.Id == loanId && l.UserBook.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("That loan was not found.");
}
