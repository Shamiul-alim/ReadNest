using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Common;

namespace ReadNest.Application.Social;

/// <summary>A stored profile photo, opened for streaming. The caller disposes the stream.</summary>
public sealed record ProfilePhotoContent(Stream Content, long Version);

/// <summary>
/// Uploaded profile photos. Images are validated by content, normalised to a square WebP without metadata and
/// kept in private storage; they are served through an endpoint that applies the profile's visibility.
/// </summary>
public sealed class ProfilePhotoService(
    IReadNestDbContextFactory dbFactory,
    IFileStore files,
    IImageProcessor images,
    IOptions<UploadOptions> uploadOptions,
    UserClock clock,
    ILogger<ProfilePhotoService> logger)
{
    public const string Container = "photos";

    /// <summary>Stored photos are squares of this many pixels (or smaller, if the original was smaller).</summary>
    public const int PhotoSize = 512;

    public long MaxBytes => uploadOptions.Value.ProfilePhotoMaxBytes;

    /// <summary>Replaces the user's photo. The previous photo is deleted only after the new one is saved.</summary>
    public async Task<ProfileDto> UploadAsync(Guid userId, Stream content, CancellationToken cancellationToken)
    {
        var bytes = await ReadLimitedAsync(content, cancellationToken);
        byte[] normalized;
        try
        {
            normalized = images.NormalizeProfilePhoto(bytes, PhotoSize);
        }
        catch (InvalidImageException ex)
        {
            throw new RequestValidationException("photo", ex.Message);
        }

        var key = await files.SaveAsync(Container, "webp", normalized, cancellationToken);
        string? previous;
        ProfileDto result;
        try
        {
            await using var db = await dbFactory.CreateAsync(cancellationToken);
            var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("Profile not found.");
            previous = profile.SetPhoto(key, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            result = ProfileService.ToDto(profile);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        if (previous is not null)
        {
            await files.DeleteAsync(previous, CancellationToken.None);
        }

        return result;
    }

    /// <summary>Removes the photo (uploaded or linked); the initials avatar is shown instead.</summary>
    public async Task<ProfileDto> RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Profile not found.");
        var previous = profile.RemovePhoto(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        if (previous is not null)
        {
            await files.DeleteAsync(previous, CancellationToken.None);
        }

        return ProfileService.ToDto(profile);
    }

    /// <summary>
    /// Opens a user's photo for a viewer. Photos of private profiles are only shown to their owner; everyone else
    /// gets null, exactly as if there were no photo.
    /// </summary>
    public async Task<ProfilePhotoContent?> OpenAsync(Guid ownerId, Guid? viewerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateAsync(cancellationToken);
        var photo = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == ownerId && p.PhotoKey != null && (p.IsProfilePublic || p.UserId == viewerId))
            .Select(p => new { p.PhotoKey, p.PhotoVersion })
            .FirstOrDefaultAsync(cancellationToken);
        if (photo?.PhotoKey is null)
        {
            return null;
        }

        var stream = files.OpenRead(photo.PhotoKey);
        if (stream is null)
        {
            logger.LogWarning("Profile photo for user {UserId} is missing from storage.", ownerId);
            return null;
        }

        return new ProfilePhotoContent(stream, photo.PhotoVersion);
    }

    private async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        var max = MaxBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new RequestValidationException("photo", $"The photo is too large. The limit is {UploadOptions.Describe(max)}.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new RequestValidationException("photo", "The file is empty.");
        }

        return buffer.ToArray();
    }
}
