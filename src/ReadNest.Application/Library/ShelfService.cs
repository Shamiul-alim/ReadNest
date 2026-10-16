using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Application.Library;

public sealed record ShelfRequest(string Name, string? Description, bool IsPublic);

public sealed record ShelfDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublic,
    string ShareSlug,
    int BookCount,
    IReadOnlyList<string?> PreviewCovers,
    DateTime UpdatedAtUtc);

public sealed record ShelfMembershipDto(Guid ShelfId, string Name, bool ContainsEntry);

public sealed record SharedShelfBookDto(Guid BookId, string Title, IReadOnlyList<string> Authors, string? CoverUrl, ReadingStatus Status, int? Rating, bool IsCatalogBook);

public sealed record SharedShelfDto(string Name, string? Description, string OwnerName, string? OwnerHandle, IReadOnlyList<SharedShelfBookDto> Books, DateTime UpdatedAtUtc);

/// <summary>
/// Custom shelves. Shelves hold library entries, so the shelf owner and the entry owner are always the same
/// user; this is validated on every operation.
/// </summary>
public sealed class ShelfService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public const int MaxShelvesPerUser = 100;

    public async Task<IReadOnlyList<ShelfDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.CustomShelves.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Name)
            .Select(s => new ShelfDto(
                s.Id,
                s.Name,
                s.Description,
                s.IsPublic,
                s.ShareSlug,
                s.Entries.Count,
                s.Entries.OrderByDescending(e => e.AddedAtUtc).Take(4).Select(e => e.UserBook.Book.CoverImageUrl).ToList(),
                s.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ShelfDto> GetAsync(Guid userId, Guid shelfId, CancellationToken cancellationToken)
    {
        var shelves = await ListAsync(userId, cancellationToken);
        return shelves.FirstOrDefault(s => s.Id == shelfId) ?? throw new NotFoundException("That shelf was not found.");
    }

    public async Task<Guid> CreateAsync(Guid userId, ShelfRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        if (await db.CustomShelves.CountAsync(s => s.UserId == userId, cancellationToken) >= MaxShelvesPerUser)
        {
            throw new ConflictException($"You can have at most {MaxShelvesPerUser} shelves.");
        }

        var shelf = CustomShelf.Create(userId, request.Name, request.Description, request.IsPublic, InputSanitizer.RandomToken(), clock.UtcNow);
        await EnsureUniqueNameAsync(db, userId, shelf.NormalizedName, exceptId: null, cancellationToken);
        db.CustomShelves.Add(shelf);
        await SaveAsync(db, cancellationToken);
        return shelf.Id;
    }

    public async Task UpdateAsync(Guid userId, Guid shelfId, ShelfRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var shelf = await LoadOwnedAsync(db, userId, shelfId, cancellationToken);
        shelf.Update(request.Name, request.Description, request.IsPublic, clock.UtcNow);
        await EnsureUniqueNameAsync(db, userId, shelf.NormalizedName, shelf.Id, cancellationToken);
        await SaveAsync(db, cancellationToken);
    }

    /// <summary>Deletes the shelf and its placements only; the books stay in the library.</summary>
    public async Task DeleteAsync(Guid userId, Guid shelfId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var shelf = await LoadOwnedAsync(db, userId, shelfId, cancellationToken);
        db.CustomShelves.Remove(shelf);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddEntryAsync(Guid userId, Guid shelfId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var shelf = await LoadOwnedAsync(db, userId, shelfId, cancellationToken);
        var entryOwned = await db.UserBooks.AnyAsync(ub => ub.Id == entryId && ub.UserId == userId, cancellationToken);
        if (!entryOwned)
        {
            throw new NotFoundException("That library entry was not found.");
        }

        if (await db.CustomShelfBooks.AnyAsync(e => e.ShelfId == shelfId && e.UserBookId == entryId, cancellationToken))
        {
            return;
        }

        var now = clock.UtcNow;
        db.CustomShelfBooks.Add(new CustomShelfBook { ShelfId = shelf.Id, UserBookId = entryId, AddedAtUtc = now });
        shelf.Update(shelf.Name, shelf.Description, shelf.IsPublic, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Already on the shelf (double submit) - adding is idempotent.
        }
    }

    public async Task RemoveEntryAsync(Guid userId, Guid shelfId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var shelf = await LoadOwnedAsync(db, userId, shelfId, cancellationToken);
        var placement = await db.CustomShelfBooks.FirstOrDefaultAsync(e => e.ShelfId == shelfId && e.UserBookId == entryId, cancellationToken);
        if (placement is null)
        {
            return;
        }

        db.CustomShelfBooks.Remove(placement);
        shelf.Update(shelf.Name, shelf.Description, shelf.IsPublic, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShelfMembershipDto>> GetMembershipAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.CustomShelves.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Name)
            .Select(s => new ShelfMembershipDto(s.Id, s.Name, s.Entries.Any(e => e.UserBookId == entryId)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>A public shelf opened through its share link. Private shelves look exactly like missing ones.</summary>
    public async Task<SharedShelfDto> GetSharedAsync(string shareSlug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shareSlug) || shareSlug.Length > 40)
        {
            throw new NotFoundException("That shelf is not available.");
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var shelf = await (
                from s in db.CustomShelves.AsNoTracking()
                join p in db.UserProfiles on s.UserId equals p.UserId
                where s.ShareSlug == shareSlug && s.IsPublic
                select new { s.Id, s.Name, s.Description, s.UpdatedAtUtc, p.DisplayName, Handle = p.IsProfilePublic ? p.Handle : null })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That shelf is not available.");

        var books = await db.CustomShelfBooks.AsNoTracking()
            .Where(e => e.ShelfId == shelf.Id)
            .OrderByDescending(e => e.AddedAtUtc)
            .Take(500)
            .Select(e => new SharedShelfBookDto(
                e.UserBook.BookId,
                e.UserBook.Book.Title,
                e.UserBook.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                e.UserBook.Book.CoverImageUrl,
                e.UserBook.Status,
                e.UserBook.Rating,
                e.UserBook.Book.Origin == BookOrigin.Catalog))
            .ToListAsync(cancellationToken);

        return new SharedShelfDto(shelf.Name, shelf.Description, shelf.DisplayName, shelf.Handle, books, shelf.UpdatedAtUtc);
    }

    private static async Task<CustomShelf> LoadOwnedAsync(IReadNestDbContext db, Guid userId, Guid shelfId, CancellationToken cancellationToken) =>
        await db.CustomShelves.FirstOrDefaultAsync(s => s.Id == shelfId && s.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("That shelf was not found.");

    private static async Task EnsureUniqueNameAsync(IReadNestDbContext db, Guid userId, string normalizedName, Guid? exceptId, CancellationToken cancellationToken)
    {
        var exists = await db.CustomShelves.AnyAsync(
            s => s.UserId == userId && s.NormalizedName == normalizedName && (exceptId == null || s.Id != exceptId),
            cancellationToken);
        if (exists)
        {
            throw new ConflictException("You already have a shelf with that name.");
        }
    }

    private static async Task SaveAsync(IReadNestDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            throw new ConflictException("You already have a shelf with that name.");
        }
    }

    internal static string NormalizeName(string name) => TextNormalizer.Key(name);
}
