using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReadNest.Domain.Entities;
using ReadNest.Infrastructure.Identity;

namespace ReadNest.Infrastructure.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.Handle).HasMaxLength(UserProfile.HandleMaxLength).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(UserProfile.DisplayNameMaxLength).IsRequired();
        builder.Property(p => p.Bio).HasMaxLength(UserProfile.BioMaxLength);
        builder.Property(p => p.ProfileImageUrl).HasMaxLength(1000);
        builder.Property(p => p.PreferredLanguage).HasMaxLength(3);
        builder.Property(p => p.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => p.Handle).IsUnique();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("UserFollows", t => t.HasCheckConstraint("CK_UserFollows_NoSelfFollow", "\"FollowerId\" <> \"FolloweeId\""));
        builder.HasKey(f => new { f.FollowerId, f.FolloweeId });
        builder.HasIndex(f => f.FolloweeId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.FolloweeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RecommendationDismissalConfiguration : IEntityTypeConfiguration<RecommendationDismissal>
{
    public void Configure(EntityTypeBuilder<RecommendationDismissal> builder)
    {
        builder.HasKey(d => new { d.UserId, d.BookId });
        builder.HasIndex(d => d.BookId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Book>().WithMany().HasForeignKey(d => d.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReadingChallengeConfiguration : IEntityTypeConfiguration<ReadingChallenge>
{
    public void Configure(EntityTypeBuilder<ReadingChallenge> builder)
    {
        builder.ToTable("ReadingChallenges", t =>
        {
            t.HasCheckConstraint("CK_ReadingChallenges_Target", "\"TargetBooks\" > 0");
            t.HasCheckConstraint("CK_ReadingChallenges_Dates", "\"EndsOn\" >= \"StartsOn\"");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(ReadingChallenge.TitleMaxLength).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(ReadingChallenge.DescriptionMaxLength);
        builder.HasIndex(c => new { c.IsPublic, c.EndsOn });
        builder.HasIndex(c => c.OwnerUserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Participants).WithOne(p => p.Challenge).HasForeignKey(p => p.ChallengeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChallengeParticipantConfiguration : IEntityTypeConfiguration<ChallengeParticipant>
{
    public void Configure(EntityTypeBuilder<ChallengeParticipant> builder)
    {
        builder.HasKey(p => new { p.ChallengeId, p.UserId });
        builder.HasIndex(p => p.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
