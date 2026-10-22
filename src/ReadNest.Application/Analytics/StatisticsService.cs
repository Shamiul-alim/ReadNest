using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Application.DTOs;
using ReadNest.Application.Library;
using ReadNest.Application.Reading;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Analytics;

public sealed record DashboardDto(
    StatusCountsDto Counts,
    int PagesReadAllTime,
    int PagesReadThisYear,
    int MinutesReadThisYear,
    StreakResult Streak,
    GoalProgressDto Goal,
    IReadOnlyList<LibraryEntryDto> CurrentlyReading,
    IReadOnlyList<LibraryEntryDto> RecentlyAdded,
    IReadOnlyList<LibraryEntryDto> RecentlyCompleted,
    IReadOnlyList<ReadingSessionDto> RecentSessions,
    IReadOnlyList<MonthlyActivityDto> MonthlyActivity,
    IReadOnlyList<NamedCountDto> TopGenres,
    IReadOnlyList<NamedCountDto> TopAuthors,
    DateOnly Today);

/// <summary>Pages read in the week starting <see cref="WeekStart"/> (Monday).</summary>
public sealed record WeeklyPagesDto(DateOnly WeekStart, int Pages);

/// <summary>One calendar day of logged reading.</summary>
public sealed record DailyActivityDto(DateOnly Date, int Pages, int Minutes);

/// <summary>Where the books started in a year ended up.</summary>
public sealed record StartOutcomesDto(int Year, int Finished, int StillReading, int Paused, int DidNotFinish)
{
    public int Total => Finished + StillReading + Paused + DidNotFinish;
}

public sealed record CompletedBookDto(Guid EntryId, Guid BookId, string Title, IReadOnlyList<string> Authors, string? CoverUrl, DateOnly CompletedOn, int? Rating, int? Pages);

public sealed record YearAnalyticsDto(
    int Year,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<MonthlyActivityDto> Months,
    int BooksCompleted,
    int PagesRead,
    int MinutesRead,
    int Sessions,
    int ReadingDays,
    decimal? AverageRating,
    IReadOnlyList<int> RatingDistribution,
    IReadOnlyList<NamedCountDto> TopGenres,
    IReadOnlyList<NamedCountDto> TopAuthors,
    CompletedBookDto? LongestBook,
    decimal AveragePagesPerReadingDay,
    StreakResult Streak,
    GoalProgressDto Goal);

/// <summary>
/// Aggregates statistics with SQL GROUP BY / SUM queries scoped to the user; it never loads a whole library
/// into memory. Favourite genres and authors are only reported once at least
/// <see cref="MinimumBooksForTasteStats"/> books with positive signals exist.
/// </summary>
public sealed class StatisticsService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public const int MinimumBooksForTasteStats = 3;

    public async Task<DashboardDto> GetDashboardAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var yearStart = new DateOnly(today.Year, 1, 1);

        var counts = await LibraryService.GetStatusCountsAsync(db, userId, cancellationToken);
        var sessions = db.ReadingSessions.Where(s => s.UserBook.UserId == userId);
        var pagesAllTime = await sessions.SumAsync(s => (int?)(s.EndPage - s.StartPage), cancellationToken) ?? 0;
        var yearTotals = await sessions
            .Where(s => s.ReadOn >= yearStart && s.ReadOn <= today)
            .GroupBy(_ => 1)
            .Select(g => new { Pages = g.Sum(s => s.EndPage - s.StartPage), Minutes = g.Sum(s => s.DurationMinutes) })
            .FirstOrDefaultAsync(cancellationToken);

        var streak = await GetStreakAsync(db, userId, today, cancellationToken);
        var goal = await GetGoalProgressAsync(db, userId, today.Year, today, cancellationToken);

        var entries = db.UserBooks.AsNoTracking().Where(ub => ub.UserId == userId);
        var currentlyReading = await entries.Where(ub => ub.Status == ReadingStatus.CurrentlyReading)
            .OrderByDescending(ub => ub.UpdatedAtUtc).Take(6).Select(LibraryProjections.Entry).ToListAsync(cancellationToken);
        var recentlyAdded = await entries.OrderByDescending(ub => ub.AddedAtUtc).Take(6).Select(LibraryProjections.Entry).ToListAsync(cancellationToken);
        var recentlyCompleted = await entries.Where(ub => ub.Status == ReadingStatus.Read)
            .OrderByDescending(ub => ub.CompletedOn).ThenByDescending(ub => ub.UpdatedAtUtc)
            .Take(6).Select(LibraryProjections.Entry).ToListAsync(cancellationToken);
        var recentSessions = await db.ReadingSessions.AsNoTracking().Where(s => s.UserBook.UserId == userId)
            .OrderByDescending(s => s.ReadOn).ThenByDescending(s => s.CreatedAtUtc)
            .Take(6).Select(ReadingSessionService.Projection).ToListAsync(cancellationToken);

        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var monthly = await GetMonthlyAsync(db, userId, firstMonth, 12, cancellationToken);

        var (topGenres, topAuthors) = await GetTasteAsync(db, userId, from: null, to: null, cancellationToken);

        return new DashboardDto(
            counts,
            pagesAllTime,
            yearTotals?.Pages ?? 0,
            yearTotals?.Minutes ?? 0,
            streak,
            goal,
            currentlyReading,
            recentlyAdded,
            recentlyCompleted,
            recentSessions,
            monthly,
            topGenres,
            topAuthors,
            today);
    }

    public async Task<YearAnalyticsDto> GetYearAnalyticsAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        year = Math.Clamp(year, ReadingGoal.MinYear, ReadingGoal.MaxYear);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        var sessionYears = await db.ReadingSessions.Where(s => s.UserBook.UserId == userId)
            .Select(s => s.ReadOn.Year).Distinct().ToListAsync(cancellationToken);
        var completionYears = await db.UserBooks.Where(ub => ub.UserId == userId && ub.CompletedOn != null)
            .Select(ub => ub.CompletedOn!.Value.Year).Distinct().ToListAsync(cancellationToken);
        var years = sessionYears.Concat(completionYears).Append(today.Year).Append(year).Distinct().OrderDescending().ToList();

        var months = await GetMonthlyAsync(db, userId, from, 12, cancellationToken);
        var yearSessions = db.ReadingSessions.Where(s => s.UserBook.UserId == userId && s.ReadOn >= from && s.ReadOn <= to);
        var readingDays = await yearSessions.Select(s => s.ReadOn).Distinct().CountAsync(cancellationToken);

        var completed = db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read && ub.CompletedOn >= from && ub.CompletedOn <= to);
        var ratings = await completed.Where(ub => ub.Rating != null)
            .GroupBy(ub => ub.Rating!.Value)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var distribution = Enumerable.Range(1, 5).Select(r => ratings.FirstOrDefault(x => x.Rating == r)?.Count ?? 0).ToList();
        var ratedCount = distribution.Sum();
        decimal? averageRating = ratedCount == 0 ? null : Math.Round(ratings.Sum(r => r.Rating * r.Count) / (decimal)ratedCount, 2);

        var longest = await completed.Where(ub => ub.TotalPages != null)
            .OrderByDescending(ub => ub.TotalPages)
            .Select(ub => new CompletedBookDto(
                ub.Id,
                ub.BookId,
                ub.Book.Title,
                ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                ub.Book.CoverImageUrl,
                ub.CompletedOn!.Value,
                ub.Rating,
                ub.TotalPages))
            .FirstOrDefaultAsync(cancellationToken);

        var (topGenres, topAuthors) = await GetTasteAsync(db, userId, from, to, cancellationToken);
        var pages = months.Sum(m => m.Pages);
        var goal = await GetGoalProgressAsync(db, userId, year, today, cancellationToken);
        var streak = await GetStreakAsync(db, userId, today, cancellationToken);

        return new YearAnalyticsDto(
            year,
            years,
            months,
            months.Sum(m => m.BooksCompleted),
            pages,
            months.Sum(m => m.Minutes),
            months.Sum(m => m.Sessions),
            readingDays,
            averageRating,
            distribution,
            topGenres,
            topAuthors,
            longest,
            readingDays == 0 ? 0 : Math.Round(pages / (decimal)readingDays, 1),
            streak,
            goal);
    }

    public async Task<IReadOnlyList<CompletedBookDto>> GetCompletedBooksAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read && ub.CompletedOn >= from && ub.CompletedOn <= to)
            .OrderByDescending(ub => ub.CompletedOn)
            .Select(ub => new CompletedBookDto(
                ub.Id,
                ub.BookId,
                ub.Book.Title,
                ub.Book.Authors.OrderBy(a => a.Position).Select(a => a.Author.Name).ToList(),
                ub.Book.CoverImageUrl,
                ub.CompletedOn!.Value,
                ub.Rating,
                ub.TotalPages))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Pages read per week for the last <paramref name="weeks"/> weeks, oldest first, ending with this week.</summary>
    public async Task<IReadOnlyList<WeeklyPagesDto>> GetWeeklyPagesAsync(Guid userId, int weeks, CancellationToken cancellationToken)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var from = thisWeek.AddDays(-7 * (weeks - 1));
        var days = await db.ReadingSessions.AsNoTracking()
            .Where(s => s.UserBook.UserId == userId && s.ReadOn >= from && s.ReadOn <= today)
            .GroupBy(s => s.ReadOn)
            .Select(g => new { Day = g.Key, Pages = g.Sum(s => s.EndPage - s.StartPage) })
            .ToListAsync(cancellationToken);
        return Enumerable.Range(0, weeks)
            .Select(i => from.AddDays(7 * i))
            .Select(start => new WeeklyPagesDto(start, days.Where(d => d.Day >= start && d.Day < start.AddDays(7)).Sum(d => d.Pages)))
            .ToList();
    }

    /// <summary>Logged pages and minutes for each day of the last <paramref name="days"/> days, oldest first.</summary>
    public async Task<IReadOnlyList<DailyActivityDto>> GetDailyActivityAsync(Guid userId, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 400);
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        var from = today.AddDays(-(days - 1));
        var rows = await db.ReadingSessions.AsNoTracking()
            .Where(s => s.UserBook.UserId == userId && s.ReadOn >= from && s.ReadOn <= today)
            .GroupBy(s => s.ReadOn)
            .Select(g => new { Day = g.Key, Pages = g.Sum(s => s.EndPage - s.StartPage), Minutes = g.Sum(s => s.DurationMinutes) })
            .ToDictionaryAsync(r => r.Day, cancellationToken);
        return Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(day => rows.TryGetValue(day, out var row) ? new DailyActivityDto(day, row.Pages, row.Minutes) : new DailyActivityDto(day, 0, 0))
            .ToList();
    }

    /// <summary>For books started in <paramref name="year"/>: how many were finished, are still going, paused or abandoned.</summary>
    public async Task<StartOutcomesDto> GetStartOutcomesAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var counts = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.StartedOn >= from && ub.StartedOn <= to)
            .GroupBy(ub => ub.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(ReadingStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        return new StartOutcomesDto(year, Count(ReadingStatus.Read), Count(ReadingStatus.CurrentlyReading), Count(ReadingStatus.Paused), Count(ReadingStatus.DidNotFinish));
    }

    internal static async Task<GoalProgressDto> GetGoalProgressAsync(IReadNestDbContext db, Guid userId, int year, DateOnly today, CancellationToken cancellationToken)
    {
        var goal = await db.ReadingGoals.AsNoTracking()
            .Where(g => g.UserId == userId && g.Year == year)
            .Select(g => new { g.TargetBooks, g.TargetPages })
            .FirstOrDefaultAsync(cancellationToken);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var completed = await db.UserBooks.CountAsync(
            ub => ub.UserId == userId && ub.Status == ReadingStatus.Read && ub.CompletedOn >= from && ub.CompletedOn <= to,
            cancellationToken);
        var pages = await db.ReadingSessions
            .Where(s => s.UserBook.UserId == userId && s.ReadOn >= from && s.ReadOn <= to)
            .SumAsync(s => (int?)(s.EndPage - s.StartPage), cancellationToken) ?? 0;
        return GoalProgressCalculator.Calculate(year, goal?.TargetBooks, goal?.TargetPages, completed, pages, today);
    }

    private static async Task<StreakResult> GetStreakAsync(IReadNestDbContext db, Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var days = await db.ReadingSessions
            .Where(s => s.UserBook.UserId == userId)
            .Select(s => s.ReadOn)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ReadingStreakCalculator.Calculate(days, today);
    }

    private static async Task<IReadOnlyList<MonthlyActivityDto>> GetMonthlyAsync(IReadNestDbContext db, Guid userId, DateOnly firstMonth, int months, CancellationToken cancellationToken)
    {
        var from = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var to = from.AddMonths(months).AddDays(-1);
        var sessionRows = await db.ReadingSessions
            .Where(s => s.UserBook.UserId == userId && s.ReadOn >= from && s.ReadOn <= to)
            .GroupBy(s => new { s.ReadOn.Year, s.ReadOn.Month })
            .Select(g => new MonthSessionRow(g.Key.Year, g.Key.Month, g.Sum(s => s.EndPage - s.StartPage), g.Sum(s => s.DurationMinutes), g.Count()))
            .ToListAsync(cancellationToken);
        var completionRows = await db.UserBooks
            .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read && ub.CompletedOn >= from && ub.CompletedOn <= to)
            .GroupBy(ub => new { ub.CompletedOn!.Value.Year, ub.CompletedOn!.Value.Month })
            .Select(g => new MonthCountRow(g.Key.Year, g.Key.Month, g.Count()))
            .ToListAsync(cancellationToken);
        return ActivityAggregator.BuildMonths(from, months, sessionRows, completionRows);
    }

    /// <summary>
    /// Genres and authors of books with a positive signal: read, currently reading, favourite or rated 4+.
    /// With a date range, only books completed in that range count.
    /// </summary>
    private static async Task<(IReadOnlyList<NamedCountDto> Genres, IReadOnlyList<NamedCountDto> Authors)> GetTasteAsync(
        IReadNestDbContext db, Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var entries = db.UserBooks.Where(ub => ub.UserId == userId);
        entries = from is { } start && to is { } end
            ? entries.Where(ub => ub.Status == ReadingStatus.Read && ub.CompletedOn >= start && ub.CompletedOn <= end)
            : entries.Where(ub => ub.Status == ReadingStatus.Read || ub.Status == ReadingStatus.CurrentlyReading || ub.IsFavorite || ub.Rating >= 4);

        if (await entries.CountAsync(cancellationToken) < MinimumBooksForTasteStats)
        {
            return ([], []);
        }

        // Order on an anonymous type: EF Core cannot translate ordering by members of a record built in the projection.
        var genres = await entries
            .SelectMany(ub => ub.Book.Genres.Select(g => g.Genre.Name))
            .GroupBy(name => name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(5)
            .Select(x => new NamedCountDto(x.Name, x.Count))
            .ToListAsync(cancellationToken);
        var authors = await entries
            .SelectMany(ub => ub.Book.Authors.Select(a => a.Author.Name))
            .GroupBy(name => name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(5)
            .Select(x => new NamedCountDto(x.Name, x.Count))
            .ToListAsync(cancellationToken);
        return (genres, authors);
    }
}
