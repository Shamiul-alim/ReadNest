using System.Globalization;

namespace ReadNest.Application.Analytics;

public sealed record StreakResult(int Current, int Longest, bool ReadToday, DateOnly? LastReadingDay);

/// <summary>
/// Reading streaks over the reader's local calendar dates. A day counts when at least one reading session
/// is recorded on it. The current streak stays alive until the end of the day after the last reading day,
/// so a reader who read yesterday but not yet today still has a streak.
/// </summary>
public static class ReadingStreakCalculator
{
    public static StreakResult Calculate(IEnumerable<DateOnly> readingDays, DateOnly today)
    {
        var days = readingDays.Where(d => d <= today).Distinct().Order().ToList();
        if (days.Count == 0)
        {
            return new StreakResult(0, 0, false, null);
        }

        var longest = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i].DayNumber - days[i - 1].DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var last = days[^1];
        var current = 0;
        if (today.DayNumber - last.DayNumber <= 1)
        {
            current = 1;
            for (var i = days.Count - 2; i >= 0 && days[i + 1].DayNumber - days[i].DayNumber == 1; i--)
            {
                current++;
            }
        }

        return new StreakResult(current, longest, last == today, last);
    }
}

public sealed record MonthlyActivityDto(int Year, int Month, int Pages, int Minutes, int Sessions, int BooksCompleted)
{
    public string Label => new DateOnly(Year, Month, 1).ToString("MMM", CultureInfo.InvariantCulture);

    public string LongLabel => new DateOnly(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}

public sealed record MonthSessionRow(int Year, int Month, int Pages, int Minutes, int Sessions);

public sealed record MonthCountRow(int Year, int Month, int Count);

public static class ActivityAggregator
{
    /// <summary>Builds a gap-free series of <paramref name="months"/> months starting at <paramref name="firstMonth"/>.</summary>
    public static IReadOnlyList<MonthlyActivityDto> BuildMonths(
        DateOnly firstMonth,
        int months,
        IEnumerable<MonthSessionRow> sessions,
        IEnumerable<MonthCountRow> completions)
    {
        var sessionLookup = sessions.ToDictionary(r => (r.Year, r.Month));
        var completionLookup = completions.ToDictionary(r => (r.Year, r.Month), r => r.Count);
        var start = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var result = new List<MonthlyActivityDto>(months);
        for (var i = 0; i < months; i++)
        {
            var month = start.AddMonths(i);
            var key = (month.Year, month.Month);
            sessionLookup.TryGetValue(key, out var row);
            result.Add(new MonthlyActivityDto(
                month.Year,
                month.Month,
                row?.Pages ?? 0,
                row?.Minutes ?? 0,
                row?.Sessions ?? 0,
                completionLookup.GetValueOrDefault(key)));
        }

        return result;
    }
}

public enum GoalPace
{
    NoGoal = 0,
    NotStarted = 1,
    Behind = 2,
    OnTrack = 3,
    Ahead = 4,
    Completed = 5,
}

public sealed record GoalProgressDto(
    int Year,
    bool HasGoal,
    int TargetBooks,
    int CompletedBooks,
    int RemainingBooks,
    decimal PercentComplete,
    int? TargetPages,
    int PagesRead,
    int ExpectedByToday,
    GoalPace Pace);

/// <summary>
/// Goal progress. Counting rule: a book counts toward year Y when its library entry has status Read and its
/// completion date (reader's local date) falls in Y. Each library entry counts at most once, so editing a
/// completion date moves a book between years instead of counting it twice. Pages read are the sum of
/// pages in reading sessions dated in Y.
/// </summary>
public static class GoalProgressCalculator
{
    public static GoalProgressDto Calculate(int year, int? targetBooks, int? targetPages, int completedBooks, int pagesRead, DateOnly today)
    {
        if (targetBooks is not > 0)
        {
            return new GoalProgressDto(year, false, 0, completedBooks, 0, 0, targetPages, pagesRead, 0, GoalPace.NoGoal);
        }

        var target = targetBooks.Value;
        var remaining = Math.Max(0, target - completedBooks);
        var percent = Math.Min(100m, Math.Round(completedBooks * 100m / target, 1));
        var expected = ExpectedByDate(year, target, today);

        GoalPace pace;
        if (completedBooks >= target)
        {
            pace = GoalPace.Completed;
        }
        else if (year > today.Year)
        {
            pace = GoalPace.NotStarted;
        }
        else if (completedBooks > expected)
        {
            pace = GoalPace.Ahead;
        }
        else if (completedBooks == expected)
        {
            pace = GoalPace.OnTrack;
        }
        else
        {
            pace = GoalPace.Behind;
        }

        return new GoalProgressDto(year, true, target, completedBooks, remaining, percent, targetPages, pagesRead, expected, pace);
    }

    /// <summary>Books that should be finished by <paramref name="today"/> for an even pace through the year.</summary>
    public static int ExpectedByDate(int year, int target, DateOnly today)
    {
        if (year < today.Year)
        {
            return target;
        }

        if (year > today.Year)
        {
            return 0;
        }

        var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        return (int)Math.Floor(target * (decimal)today.DayOfYear / daysInYear);
    }
}
