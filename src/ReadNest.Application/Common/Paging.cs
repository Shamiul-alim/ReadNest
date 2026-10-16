using Microsoft.EntityFrameworkCore;

namespace ReadNest.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int pageSize = Paging.DefaultPageSize) => new([], 1, pageSize, 0);
}

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPage = 10_000;

    public static (int Page, int PageSize) Normalize(int page, int pageSize, int maxPageSize = MaxPageSize) =>
        (Math.Clamp(page, 1, MaxPage), pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, maxPageSize));

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}
