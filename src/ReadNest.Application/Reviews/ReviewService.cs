using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Application.Library;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Reviews;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    /// <summary>When false, reviews can only be private and no public reviews are listed.</summary>
    public bool PublicReviews { get; set; } = true;
}

public sealed record ReviewRequest(int? Rating, string? Text, ReviewVisibility Visibility);

public sealed record MyReviewDto(
    Guid EntryId,
    Guid BookId,
    string BookTitle,
    string? CoverUrl,
    int? Rating,
    string? Review,
    ReviewVisibility Visibility,
    DateTime? UpdatedAtUtc);

/// <summary>A published review. Contains only what the author chose to publish.</summary>
public sealed record PublicReviewDto(
    Guid BookId,
    string BookTitle,
    string? CoverUrl,
    string ReviewerName,
    string? ReviewerHandle,
    int? Rating,
    string Review,
    DateTime UpdatedAtUtc);

/// <summary>
/// Rating and review of a library entry. The rating lives on the library entry, so a user has exactly
/// one rating per book.
/// </summary>
public sealed class ReviewService(IReadNestDbContextFactory dbFactory, UserClock clock, IOptions<FeatureOptions> features)
{
    public bool PublicReviewsEnabled => features.Value.PublicReviews;

    public async Task SetRatingAsync(Guid userId, Guid entryId, int? rating, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LibraryService.LoadOwnedAsync(db, userId, entryId, cancellationToken);
        entry.SetRating(rating, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveReviewAsync(Guid userId, Guid entryId, ReviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Visibility == ReviewVisibility.Public && !PublicReviewsEnabled)
        {
            throw new RequestValidationException("visibility", "Public reviews are disabled on this site.");
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LibraryService.LoadOwnedAsync(db, userId, entryId, cancellationToken);
        var now = clock.UtcNow;
        entry.SetRating(request.Rating, now);
        entry.SetReview(request.Text, request.Visibility, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deletes the written review. The star rating is kept; clear it separately if wanted.</summary>
    public async Task DeleteReviewAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var entry = await LibraryService.LoadOwnedAsync(db, userId, entryId, cancellationToken);
        entry.DeleteReview(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<MyReviewDto>> GetMyReviewsAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && (ub.Rating != null || ub.Review != null))
            .OrderByDescending(ub => ub.ReviewUpdatedAtUtc ?? ub.UpdatedAtUtc)
            .Select(ub => new MyReviewDto(ub.Id, ub.BookId, ub.Book.Title, ub.Book.CoverImageUrl, ub.Rating, ub.Review, ub.ReviewVisibility, ub.ReviewUpdatedAtUtc))
            .ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<PublicReviewDto>> GetPublicReviewsForBookAsync(Guid bookId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!PublicReviewsEnabled)
        {
            return PagedResult<PublicReviewDto>.Empty(pageSize);
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await PublicReviews(db, bookId: bookId).ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<PublicReviewDto>> GetRecentPublicReviewsAsync(int page, int pageSize, Guid? byUserId, CancellationToken cancellationToken)
    {
        if (!PublicReviewsEnabled)
        {
            return PagedResult<PublicReviewDto>.Empty(pageSize);
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await PublicReviews(db, authorId: byUserId).ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Published reviews on catalogue books. The reviewer handle is exposed only when their profile is public.
    /// </summary>
    /// <remarks>All filters are applied before projecting, so the whole query translates to SQL.</remarks>
    private static IQueryable<PublicReviewDto> PublicReviews(IReadNestDbContext db, Guid? authorId = null, Guid? bookId = null) =>
        from ub in db.UserBooks.AsNoTracking()
        join profile in db.UserProfiles on ub.UserId equals profile.UserId
        where ub.ReviewVisibility == ReviewVisibility.Public
              && ub.Review != null
              && ub.Book.Origin == BookOrigin.Catalog
              && (authorId == null || ub.UserId == authorId)
              && (bookId == null || ub.BookId == bookId)
        orderby ub.ReviewUpdatedAtUtc descending
        select new PublicReviewDto(
            ub.BookId,
            ub.Book.Title,
            ub.Book.CoverImageUrl,
            profile.DisplayName,
            profile.IsProfilePublic ? profile.Handle : null,
            ub.Rating,
            ub.Review!,
            ub.ReviewUpdatedAtUtc ?? ub.UpdatedAtUtc);
}
