using System.Text;
using Microsoft.EntityFrameworkCore;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;
using ReadNest.Domain.Entities;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;

namespace ReadNest.Application.Social;

public sealed record ProfileDto(
    Guid UserId,
    string Handle,
    string DisplayName,
    string? Bio,
    string? ProfileImageUrl,
    string? PreferredLanguage,
    string TimeZoneId,
    bool IsProfilePublic,
    bool IsLibraryPublic,
    bool HasUploadedPhoto = false);

public sealed record ProfileUpdateRequest(string DisplayName, string Handle, string? Bio, string? ProfileImageUrl);

public sealed record PreferencesRequest(string? PreferredLanguage, string TimeZoneId);

public sealed record PrivacyRequest(bool IsProfilePublic, bool IsLibraryPublic);

public sealed record ReaderPreferencesDto(ReaderTheme Theme, int FontScale, int LineHeight, ReaderWidth Width, ReaderFlow Flow)
{
    public static readonly ReaderPreferencesDto Default = new(ReaderTheme.Paper, 100, 160, ReaderWidth.Medium, ReaderFlow.Paginated);
}

/// <summary>
/// User profiles. A profile is created at registration; <see cref="GetOrCreateAsync"/> also creates one lazily
/// for accounts created another way (for example through the token API).
/// </summary>
public sealed class ProfileService(IReadNestDbContextFactory dbFactory, UserClock clock, IFileStore files)
{
    public async Task<ProfileDto> GetOrCreateAsync(Guid userId, string? suggestedName, string? timeZoneId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var existing = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var displayName = string.IsNullOrWhiteSpace(suggestedName) ? "Reader" : suggestedName.Trim();
        if (displayName.Length > UserProfile.DisplayNameMaxLength)
        {
            displayName = displayName[..UserProfile.DisplayNameMaxLength];
        }

        var zone = UserClock.IsKnownTimeZone(timeZoneId) ? timeZoneId! : "UTC";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var handle = await GenerateHandleAsync(db, displayName, attempt, cancellationToken);
            var profile = UserProfile.Create(userId, handle, displayName, zone, clock.UtcNow);
            db.UserProfiles.Add(profile);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return ToDto(profile);
            }
            catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
            {
                if (db is DbContext context)
                {
                    context.ChangeTracker.Clear();
                }

                // Either the profile was created concurrently or the handle was taken; check and retry.
                var created = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (created is not null)
                {
                    return ToDto(created);
                }
            }
        }

        throw new ConflictException("Could not create a profile. Please try again.");
    }

    public async Task<ProfileDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return profile is null ? null : ToDto(profile);
    }

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, ProfileUpdateRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await LoadAsync(db, userId, cancellationToken);
        var now = clock.UtcNow;
        string? replacedPhotoKey = null;
        string? imageUrl;
        if (profile.PhotoKey is not null
            && (string.IsNullOrWhiteSpace(request.ProfileImageUrl) || request.ProfileImageUrl == PhotoUrl(profile)))
        {
            // An uploaded photo is managed through ProfilePhotoService; leaving the field alone keeps it.
            imageUrl = null;
        }
        else
        {
            imageUrl = InputSanitizer.RequireSafeUrlOrNull(request.ProfileImageUrl, "profileImageUrl");
            if (imageUrl is not null && profile.PhotoKey is not null)
            {
                // Switching from an uploaded photo to an image link.
                replacedPhotoKey = profile.RemovePhoto(now);
            }
        }

        profile.UpdateProfile(request.DisplayName, request.Handle, request.Bio, imageUrl, now);
        if (await db.UserProfiles.AnyAsync(p => p.Handle == profile.Handle && p.UserId != userId, cancellationToken))
        {
            throw new ConflictException("That handle is already taken.");
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            throw new ConflictException("That handle is already taken.");
        }

        if (replacedPhotoKey is not null)
        {
            await files.DeleteAsync(replacedPhotoKey, CancellationToken.None);
        }

        return ToDto(profile);
    }

    public async Task<ReaderPreferencesDto> GetReaderPreferencesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        return await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new ReaderPreferencesDto(p.ReaderTheme, p.ReaderFontScale, p.ReaderLineHeight, p.ReaderWidth, p.ReaderFlow))
            .FirstOrDefaultAsync(cancellationToken)
            ?? ReaderPreferencesDto.Default;
    }

    public async Task<ReaderPreferencesDto> UpdateReaderPreferencesAsync(Guid userId, ReaderPreferencesDto request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await LoadAsync(db, userId, cancellationToken);
        try
        {
            profile.UpdateReaderPreferences(request.Theme, request.FontScale, request.LineHeight, request.Width, request.Flow, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw RequestValidationException.From(ex);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReaderPreferencesDto(profile.ReaderTheme, profile.ReaderFontScale, profile.ReaderLineHeight, profile.ReaderWidth, profile.ReaderFlow);
    }

    public async Task<ProfileDto> UpdatePreferencesAsync(Guid userId, PreferencesRequest request, CancellationToken cancellationToken)
    {
        if (!UserClock.IsKnownTimeZone(request.TimeZoneId))
        {
            throw new RequestValidationException("timeZoneId", "Choose a valid time zone.");
        }

        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await LoadAsync(db, userId, cancellationToken);
        profile.UpdatePreferences(request.PreferredLanguage, request.TimeZoneId.Trim(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    public async Task<ProfileDto> UpdatePrivacyAsync(Guid userId, PrivacyRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await LoadAsync(db, userId, cancellationToken);
        profile.UpdatePrivacy(request.IsProfilePublic, request.IsLibraryPublic, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile);
    }

    /// <summary>Builds a URL-friendly handle from a display name ("Ada Lovelace" -> "ada-lovelace").</summary>
    public static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if ((c is ' ' or '-' or '_' or '.') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 24)
        {
            slug = slug[..24].Trim('-');
        }

        return slug.Length < UserProfile.HandleMinLength ? "reader" : slug;
    }

    private static async Task<string> GenerateHandleAsync(IReadNestDbContext db, string displayName, int attempt, CancellationToken cancellationToken)
    {
        var baseHandle = Slugify(displayName);
        if (attempt == 0 && !await db.UserProfiles.AnyAsync(p => p.Handle == baseHandle, cancellationToken))
        {
            return baseHandle;
        }

        return $"{baseHandle}-{Random.Shared.Next(1000, 99999)}";
    }

    private static async Task<UserProfile> LoadAsync(IReadNestDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Profile not found.");

    internal static ProfileDto ToDto(UserProfile p) =>
        new(p.UserId, p.Handle, p.DisplayName, p.Bio, PhotoUrl(p), p.PreferredLanguage, p.TimeZoneId, p.IsProfilePublic, p.IsLibraryPublic, p.PhotoKey is not null);

    private static string? PhotoUrl(UserProfile p) => UserProfile.PhotoUrlFor(p.UserId, p.PhotoKey, p.PhotoVersion, p.ProfileImageUrl);
}
