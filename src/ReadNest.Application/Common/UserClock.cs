using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;

namespace ReadNest.Application.Common;

/// <summary>
/// Resolves "today" in the reader's own time zone. Reading dates are calendar dates in the reader's
/// local time, while event timestamps are stored in UTC.
/// </summary>
public sealed class UserClock(TimeProvider timeProvider)
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<DateOnly> GetTodayAsync(IReadNestDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var timeZoneId = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        return TodayIn(timeZoneId);
    }

    public DateOnly TodayIn(string? timeZoneId) => LocalDate(timeProvider.GetUtcNow(), timeZoneId);

    /// <summary>The current wall-clock time in the reader's time zone (UTC when unknown).</summary>
    public DateTime LocalNow(string? timeZoneId) => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ResolveTimeZone(timeZoneId)).DateTime;

    public static DateOnly LocalDate(DateTimeOffset instant, string? timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    public static bool IsKnownTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
