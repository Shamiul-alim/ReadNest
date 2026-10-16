using ReadNest.Application.Analytics;

namespace ReadNest.UnitTests.Application;

public sealed class ReadingStreakCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    [Fact]
    public void No_reading_means_no_streak()
    {
        var result = ReadingStreakCalculator.Calculate([], Today);

        Assert.Equal(0, result.Current);
        Assert.Equal(0, result.Longest);
        Assert.False(result.ReadToday);
    }

    [Fact]
    public void Consecutive_days_ending_today_form_the_current_streak()
    {
        var days = new[] { Today, Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-5) };

        var result = ReadingStreakCalculator.Calculate(days, Today);

        Assert.Equal(3, result.Current);
        Assert.Equal(3, result.Longest);
        Assert.True(result.ReadToday);
    }

    [Fact]
    public void Streak_survives_until_the_end_of_the_next_day()
    {
        var result = ReadingStreakCalculator.Calculate([Today.AddDays(-1), Today.AddDays(-2)], Today);

        Assert.Equal(2, result.Current);
        Assert.False(result.ReadToday);
    }

    [Fact]
    public void Streak_breaks_after_a_missed_day()
    {
        var result = ReadingStreakCalculator.Calculate([Today.AddDays(-2), Today.AddDays(-3)], Today);

        Assert.Equal(0, result.Current);
        Assert.Equal(2, result.Longest);
    }

    [Fact]
    public void Duplicate_days_and_month_boundaries_are_handled()
    {
        // 2026-02-28 -> 2026-03-01 -> 2026-03-02 crosses a month boundary (non-leap year).
        var days = new[] { new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), Today };

        var result = ReadingStreakCalculator.Calculate(days, Today);

        Assert.Equal(3, result.Current);
    }

    [Fact]
    public void Future_dates_are_ignored()
    {
        var result = ReadingStreakCalculator.Calculate([Today.AddDays(1), Today], Today);

        Assert.Equal(1, result.Current);
    }
}

public sealed class GoalProgressCalculatorTests
{
    [Fact]
    public void Without_goal_reports_completed_books_only()
    {
        var result = GoalProgressCalculator.Calculate(2026, null, null, 4, 900, new DateOnly(2026, 6, 1));

        Assert.False(result.HasGoal);
        Assert.Equal(4, result.CompletedBooks);
        Assert.Equal(GoalPace.NoGoal, result.Pace);
    }

    [Fact]
    public void Remaining_and_percent_follow_the_example()
    {
        var result = GoalProgressCalculator.Calculate(2027, 24, null, 7, 0, new DateOnly(2027, 3, 1));

        Assert.Equal(17, result.RemainingBooks);
        Assert.Equal(29.2m, result.PercentComplete);
    }

    [Fact]
    public void Expected_pace_is_proportional_to_the_day_of_year()
    {
        // Day 182 of 365: 24 * 182 / 365 = 11.96 -> 11 books expected.
        Assert.Equal(11, GoalProgressCalculator.ExpectedByDate(2026, 24, new DateOnly(2026, 7, 1)));
        Assert.Equal(24, GoalProgressCalculator.ExpectedByDate(2025, 24, new DateOnly(2026, 7, 1)));
        Assert.Equal(0, GoalProgressCalculator.ExpectedByDate(2027, 24, new DateOnly(2026, 7, 1)));
    }

    [Theory]
    [InlineData(12, GoalPace.Ahead)]
    [InlineData(11, GoalPace.OnTrack)]
    [InlineData(5, GoalPace.Behind)]
    [InlineData(24, GoalPace.Completed)]
    [InlineData(30, GoalPace.Completed)]
    public void Pace_is_classified(int completed, GoalPace expected)
    {
        var result = GoalProgressCalculator.Calculate(2026, 24, null, completed, 0, new DateOnly(2026, 7, 1));

        Assert.Equal(expected, result.Pace);
        Assert.True(result.PercentComplete <= 100);
        Assert.True(result.RemainingBooks >= 0);
    }

    [Fact]
    public void Leap_years_use_366_days()
    {
        Assert.Equal(12, GoalProgressCalculator.ExpectedByDate(2028, 24, new DateOnly(2028, 7, 1)));
    }
}

public sealed class ActivityAggregatorTests
{
    [Fact]
    public void Builds_a_gap_free_series_across_a_year_boundary()
    {
        var sessions = new[] { new MonthSessionRow(2025, 12, 120, 60, 3), new MonthSessionRow(2026, 2, 50, 30, 1) };
        var completions = new[] { new MonthCountRow(2026, 2, 2) };

        var months = ActivityAggregator.BuildMonths(new DateOnly(2025, 11, 15), 4, sessions, completions);

        Assert.Equal(["2025-11", "2025-12", "2026-01", "2026-02"], months.Select(m => $"{m.Year}-{m.Month:00}"));
        Assert.Equal([0, 120, 0, 50], months.Select(m => m.Pages));
        Assert.Equal([0, 0, 0, 2], months.Select(m => m.BooksCompleted));
        Assert.Equal("Dec", months[1].Label);
    }
}
