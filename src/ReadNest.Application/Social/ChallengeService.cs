using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;

namespace ReadNest.Application.Social;

public sealed record ChallengeRequest(string Title, string? Description, int TargetBooks, DateOnly StartsOn, DateOnly EndsOn, bool IsPublic);

public sealed record ChallengeSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    int TargetBooks,
    DateOnly StartsOn,
    DateOnly EndsOn,
    bool IsPublic,
    int ParticipantCount,
    bool IsParticipant,
    bool IsOwner);

public sealed record ChallengeParticipantDto(string DisplayName, string? Handle, int BooksCompleted, bool IsYou);

public sealed record ChallengeDetailsDto(ChallengeSummaryDto Challenge, IReadOnlyList<ChallengeParticipantDto> Participants, int? MyBooksCompleted, DateOnly Today);

/// <summary>
/// Reading challenges: finish N books between two dates. Progress is derived from library entries with status
/// Read and a completion date inside the challenge window. Participants with private profiles are shown anonymously.
/// </summary>
public sealed class ChallengeService(IReadNestDbContextFactory dbFactory, UserClock clock)
{
    public async Task<Guid> CreateAsync(Guid userId, ChallengeRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        if (await db.ReadingChallenges.CountAsync(c => c.OwnerUserId == userId, cancellationToken) >= 50)
        {
            throw new ConflictException("You can create at most 50 challenges.");
        }

        var challenge = ReadingChallenge.Create(userId, request.Title, request.Description, request.TargetBooks, request.StartsOn, request.EndsOn, request.IsPublic, clock.UtcNow);
        db.ReadingChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        return challenge.Id;
    }

    public async Task<IReadOnlyList<ChallengeSummaryDto>> ListAsync(Guid? viewerId, bool mineOnly, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var challenges = db.ReadingChallenges.AsNoTracking();
        challenges = mineOnly && viewerId is { } me
            ? challenges.Where(c => c.Participants.Any(p => p.UserId == me))
            : challenges.Where(c => c.IsPublic);

        return await challenges
            .OrderByDescending(c => c.EndsOn)
            .Take(100)
            .Select(c => new ChallengeSummaryDto(
                c.Id,
                c.Title,
                c.Description,
                c.TargetBooks,
                c.StartsOn,
                c.EndsOn,
                c.IsPublic,
                c.Participants.Count,
                viewerId != null && c.Participants.Any(p => p.UserId == viewerId),
                viewerId != null && c.OwnerUserId == viewerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChallengeDetailsDto> GetAsync(Guid challengeId, Guid? viewerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var challenge = await db.ReadingChallenges.AsNoTracking()
            .Where(c => c.Id == challengeId && (c.IsPublic || (viewerId != null && c.Participants.Any(p => p.UserId == viewerId))))
            .Select(c => new ChallengeSummaryDto(
                c.Id,
                c.Title,
                c.Description,
                c.TargetBooks,
                c.StartsOn,
                c.EndsOn,
                c.IsPublic,
                c.Participants.Count,
                viewerId != null && c.Participants.Any(p => p.UserId == viewerId),
                viewerId != null && c.OwnerUserId == viewerId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That challenge is not available.");

        // Left join: a participant without a profile row is still counted, and shown anonymously.
        var participants = await (
                from p in db.ChallengeParticipants.AsNoTracking()
                join profile in db.UserProfiles on p.UserId equals profile.UserId into profiles
                from profile in profiles.DefaultIfEmpty()
                where p.ChallengeId == challengeId
                select new
                {
                    p.UserId,
                    DisplayName = profile == null ? "Reader" : profile.DisplayName,
                    Handle = profile == null ? null : profile.Handle,
                    IsProfilePublic = profile != null && profile.IsProfilePublic,
                    Completed = db.UserBooks.Count(ub => ub.UserId == p.UserId
                        && ub.Status == ReadingStatus.Read
                        && ub.CompletedOn >= challenge.StartsOn
                        && ub.CompletedOn <= challenge.EndsOn),
                })
            .OrderByDescending(x => x.Completed)
            .Take(200)
            .ToListAsync(cancellationToken);

        var rows = participants
            .Select(p => new ChallengeParticipantDto(
                p.IsProfilePublic || p.UserId == viewerId ? p.DisplayName : "Private reader",
                p.IsProfilePublic ? p.Handle : null,
                p.Completed,
                p.UserId == viewerId))
            .ToList();
        var mine = participants.FirstOrDefault(p => p.UserId == viewerId)?.Completed;
        var today = viewerId is { } viewer ? await clock.GetTodayAsync(db, viewer, cancellationToken) : clock.TodayIn("UTC");
        return new ChallengeDetailsDto(challenge, rows, mine, today);
    }

    public async Task JoinAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var challenge = await db.ReadingChallenges
            .FirstOrDefaultAsync(c => c.Id == challengeId && (c.IsPublic || c.OwnerUserId == userId), cancellationToken)
            ?? throw new NotFoundException("That challenge is not available.");
        if (await db.ChallengeParticipants.AnyAsync(p => p.ChallengeId == challenge.Id && p.UserId == userId, cancellationToken))
        {
            return;
        }

        var today = await clock.GetTodayAsync(db, userId, cancellationToken);
        if (today > challenge.EndsOn)
        {
            throw new ConflictException("This challenge has already ended.");
        }

        db.ChallengeParticipants.Add(new ChallengeParticipant { ChallengeId = challenge.Id, UserId = userId, JoinedAtUtc = clock.UtcNow });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Already joined.
        }
    }

    public async Task LeaveAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var isOwner = await db.ReadingChallenges.AnyAsync(c => c.Id == challengeId && c.OwnerUserId == userId, cancellationToken);
        if (isOwner)
        {
            throw new ConflictException("You created this challenge. Delete it instead of leaving.");
        }

        var participation = await db.ChallengeParticipants.FirstOrDefaultAsync(p => p.ChallengeId == challengeId && p.UserId == userId, cancellationToken);
        if (participation is not null)
        {
            db.ChallengeParticipants.Remove(participation);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var challenge = await db.ReadingChallenges.FirstOrDefaultAsync(c => c.Id == challengeId && c.OwnerUserId == userId, cancellationToken)
            ?? throw new NotFoundException("That challenge is not available.");
        db.ReadingChallenges.Remove(challenge);
        await db.SaveChangesAsync(cancellationToken);
    }
}
