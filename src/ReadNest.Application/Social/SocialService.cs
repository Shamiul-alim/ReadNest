using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Application.Library;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Social;

public sealed record PersonDto(string Handle, string DisplayName, string? ProfileImageUrl, string? Bio, bool IsPublic);

public sealed record PublicShelfLinkDto(string Name, string ShareSlug, int BookCount, IReadOnlyList<ShelfPreviewDto>? Previews = null);

public sealed record PublicBookDto(Guid BookId, string Title, IReadOnlyList<string> Authors, string? CoverUrl, ReadingStatus Status, int? Rating, bool IsCatalogBook);

public sealed record PublicProfileDto(
    Guid UserId,
    string Handle,
    string DisplayName,
    string? Bio,
    string? ProfileImageUrl,
    bool IsOwnProfile,
    bool IsFollowing,
    int Followers,
    int Following,
    bool IsLibraryPublic,
    int? BooksRead,
    IReadOnlyList<PublicBookDto> CurrentlyReading,
    IReadOnlyList<PublicShelfLinkDto> PublicShelves);

/// <summary>
/// Following and public profiles. Visibility is checked on the server for every request: a private profile is
/// indistinguishable from a missing one, and private notes, reviews, loans and account data are never returned.
/// </summary>
public sealed class SocialService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public async Task<PublicProfileDto> GetPublicProfileAsync(string handle, Guid? viewerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await FindVisibleProfileAsync(db, handle, viewerId, cancellationToken);
        var isOwn = viewerId == profile.UserId;

        var followers = await db.UserFollows.CountAsync(f => f.FolloweeId == profile.UserId, cancellationToken);
        var following = await db.UserFollows.CountAsync(f => f.FollowerId == profile.UserId, cancellationToken);
        var isFollowing = viewerId is { } viewer && await db.UserFollows.AnyAsync(f => f.FollowerId == viewer && f.FolloweeId == profile.UserId, cancellationToken);

        int? booksRead = null;
        IReadOnlyList<PublicBookDto> currentlyReading = [];
        if (profile.IsLibraryPublic || isOwn)
        {
            booksRead = await db.UserBooks.CountAsync(ub => ub.UserId == profile.UserId && ub.Status == ReadingStatus.Read, cancellationToken);
            currentlyReading = await PublicBooks(db, profile.UserId, ReadingStatus.CurrentlyReading)
                .Take(6)
                .ToListAsync(cancellationToken);
        }

        var shelves = await db.CustomShelves.AsNoTracking()
            .Where(s => s.UserId == profile.UserId && s.IsPublic)
            .OrderBy(s => s.Name)
            .Select(s => new PublicShelfLinkDto(s.Name, s.ShareSlug, s.Entries.Count, s.Entries.OrderByDescending(e => e.AddedAtUtc).Take(4).Select(e => new ShelfPreviewDto(e.UserBook.Book.Title, e.UserBook.Book.CoverImageUrl)).ToList()))
            .ToListAsync(cancellationToken);

        return new PublicProfileDto(
            profile.UserId,
            profile.Handle,
            profile.DisplayName,
            profile.Bio,
            UserProfile.PhotoUrlFor(profile.UserId, profile.PhotoKey, profile.PhotoVersion, profile.ProfileImageUrl),
            isOwn,
            isFollowing,
            followers,
            following,
            profile.IsLibraryPublic,
            booksRead,
            currentlyReading,
            shelves);
    }

    public async Task<PagedResult<PublicBookDto>> GetPublicLibraryAsync(string handle, Guid? viewerId, ReadingStatus? status, int page, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await FindVisibleProfileAsync(db, handle, viewerId, cancellationToken);
        if (!profile.IsLibraryPublic && viewerId != profile.UserId)
        {
            throw new NotFoundException("This reading list is private.");
        }

        return await PublicBooks(db, profile.UserId, status).ToPagedResultAsync(page, 24, cancellationToken);
    }

    public async Task FollowAsync(Guid userId, string handle, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var target = await FindVisibleProfileAsync(db, handle, userId, cancellationToken);
        if (target.UserId == userId)
        {
            throw new RequestValidationException("handle", "You cannot follow yourself.");
        }

        if (await db.UserFollows.AnyAsync(f => f.FollowerId == userId && f.FolloweeId == target.UserId, cancellationToken))
        {
            return;
        }

        db.UserFollows.Add(UserFollow.Create(userId, target.UserId, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Already following (double submit); following is idempotent.
        }
    }

    public async Task UnfollowAsync(Guid userId, string handle, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var normalized = handle.Trim().ToLowerInvariant();
        var follow = await (
                from f in db.UserFollows
                join p in db.UserProfiles on f.FolloweeId equals p.UserId
                where f.FollowerId == userId && p.Handle == normalized
                select f)
            .FirstOrDefaultAsync(cancellationToken);
        if (follow is not null)
        {
            db.UserFollows.Remove(follow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PersonDto>> GetFollowingAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await (
                from f in db.UserFollows.AsNoTracking()
                join profile in db.UserProfiles on f.FolloweeId equals profile.UserId into profiles
                from p in profiles.DefaultIfEmpty()
                where f.FollowerId == userId
                orderby p == null ? "Reader" : p.DisplayName
                select new PersonDto(
                    p == null ? string.Empty : p.Handle,
                    p == null ? "Reader" : p.DisplayName,
                    p == null ? null : UserProfile.PhotoUrlFor(p.UserId, p.IsProfilePublic ? p.PhotoKey : null, p.PhotoVersion, p.ProfileImageUrl),
                    p != null && p.IsProfilePublic ? p.Bio : null,
                    p != null && p.IsProfilePublic))
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PersonDto>> GetFollowersAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await (
                from f in db.UserFollows.AsNoTracking()
                join profile in db.UserProfiles on f.FollowerId equals profile.UserId into profiles
                from p in profiles.DefaultIfEmpty()
                where f.FolloweeId == userId
                orderby p == null ? "Reader" : p.DisplayName
                select new PersonDto(
                    p == null ? string.Empty : p.Handle,
                    p == null ? "Reader" : p.DisplayName,
                    p == null ? null : UserProfile.PhotoUrlFor(p.UserId, p.IsProfilePublic ? p.PhotoKey : null, p.PhotoVersion, p.ProfileImageUrl),
                    p != null && p.IsProfilePublic ? p.Bio : null,
                    p != null && p.IsProfilePublic))
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Finds readers with public profiles by display name or handle.</summary>
    public async Task<IReadOnlyList<PersonDto>> SearchPeopleAsync(string? search, Guid? excludeUserId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var people = db.UserProfiles.AsNoTracking().Where(p => p.IsProfilePublic);
        if (excludeUserId is { } exclude)
        {
            people = people.Where(p => p.UserId != exclude);
        }

        if (InputSanitizer.SearchTerm(search) is { } term)
        {
            var lowered = term.ToLowerInvariant();
            people = people.Where(p => p.Handle.Contains(lowered) || p.DisplayName.ToLower().Contains(lowered));
        }

        return await people
            .OrderBy(p => p.DisplayName)
            .Take(30)
            .Select(p => new PersonDto(p.Handle, p.DisplayName, UserProfile.PhotoUrlFor(p.UserId, p.PhotoKey, p.PhotoVersion, p.ProfileImageUrl), p.Bio, true))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<PublicBookDto> PublicBooks(IReadNestDbContext db, Guid ownerId, ReadingStatus? status = null) =>
        db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == ownerId && (status == null || ub.Status == status))
            .OrderByDescending(ub => ub.UpdatedAtUtc)
            .Select(ub => new PublicBookDto(
                ub.BookId,
                ub.Book.Title,
                ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                ub.Book.CoverImageUrl,
                ub.Status,
                ub.Rating,
                ub.Book.Origin == BookOrigin.Catalog));

    private static async Task<UserProfile> FindVisibleProfileAsync(IReadNestDbContext db, string handle, Guid? viewerId, CancellationToken cancellationToken)
    {
        var normalized = (handle ?? string.Empty).Trim().ToLowerInvariant();
        if (!UserProfile.IsValidHandle(normalized))
        {
            throw new NotFoundException("That profile is not available.");
        }

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Handle == normalized, cancellationToken);
        if (profile is null || (!profile.IsProfilePublic && profile.UserId != viewerId))
        {
            throw new NotFoundException("That profile is not available.");
        }

        return profile;
    }
}
