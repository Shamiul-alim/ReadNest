using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Analytics;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;

namespace ReadNest.Application.Goals;

public sealed record GoalRequest(int TargetBooks, int? TargetPages);

public sealed record GoalSummaryDto(Guid Id, int Year, int TargetBooks, int? TargetPages);

public sealed class GoalService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public async Task<IReadOnlyList<GoalSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.ReadingGoals.AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.Year)
            .Select(g => new GoalSummaryDto(g.Id, g.Year, g.TargetBooks, g.TargetPages))
            .ToListAsync(cancellationToken);
    }

    public async Task<GoalProgressDto> GetProgressAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        ValidateYear(year);
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        return await StatisticsService.GetGoalProgressAsync(db, userId, year, today, cancellationToken);
    }

    /// <summary>Creates or replaces the goal for <paramref name="year"/> (one goal per user and year).</summary>
    public async Task<GoalProgressDto> SetGoalAsync(Guid userId, int year, GoalRequest request, CancellationToken cancellationToken)
    {
        ValidateYear(year);
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var now = clock.UtcNow;
        var goal = await db.ReadingGoals.FirstOrDefaultAsync(g => g.UserId == userId && g.Year == year, cancellationToken);
        if (goal is null)
        {
            db.ReadingGoals.Add(ReadingGoal.Create(userId, year, request.TargetBooks, request.TargetPages, now));
        }
        else
        {
            goal.Update(request.TargetBooks, request.TargetPages, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            throw new ConflictException("A goal for that year was just created. Refresh and try again.");
        }

        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        return await StatisticsService.GetGoalProgressAsync(db, userId, year, today, cancellationToken);
    }

    public async Task DeleteGoalAsync(Guid userId, int year, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var goal = await db.ReadingGoals.FirstOrDefaultAsync(g => g.UserId == userId && g.Year == year, cancellationToken)
            ?? throw new NotFoundException("There is no goal for that year.");
        db.ReadingGoals.Remove(goal);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateYear(int year)
    {
        if (year is < ReadingGoal.MinYear or > ReadingGoal.MaxYear)
        {
            throw new RequestValidationException("year", $"Year must be between {ReadingGoal.MinYear} and {ReadingGoal.MaxYear}.");
        }
    }
}
