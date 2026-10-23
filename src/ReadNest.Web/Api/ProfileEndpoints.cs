using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using ReadNest.Application.Common;
using ReadNest.Application.Social;
using ReadNest.Web.Authorization;
using ReadNest.Web.Configuration;

namespace ReadNest.Web.Api;

internal static class ProfileEndpoints
{
    /// <summary>Endpoints for the signed-in user's own profile (inside the authenticated API group).</summary>
    public static void MapProfileEndpoints(this RouteGroupBuilder api)
    {
        var profile = api.MapGroup("/profile").WithTags("Profile");
        var limits = UploadLimits.From(api);

        profile.MapGet("/", async Task<Results<Ok<ProfileDto>, NotFound>> (ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
                await service.GetAsync(user.GetRequiredUserId(), ct) is { } dto ? TypedResults.Ok(dto) : TypedResults.NotFound())
            .WithSummary("Your profile. profileImageUrl points at your uploaded photo when you have one.");

        profile.MapPut("/photo", async Task<Ok<ProfileDto>> (IFormFile photo, ClaimsPrincipal user, ProfilePhotoService service, CancellationToken ct) =>
            {
                if (photo.Length == 0 || photo.Length > service.MaxBytes)
                {
                    throw new RequestValidationException("photo", $"Upload a JPEG, PNG or WebP image up to {Application.Abstractions.UploadOptions.Describe(service.MaxBytes)}.");
                }

                await using var stream = photo.OpenReadStream();
                return TypedResults.Ok(await service.UploadAsync(user.GetRequiredUserId(), stream, ct));
            })
            .DisableAntiforgery()
            .RequireAuthorization(AuthPolicies.BearerOnly)
            .RequireRateLimiting(RateLimitPolicies.Import)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(limits.PhotoRequestBytes))
            .WithSummary("Upload or replace your profile photo (multipart field 'photo'; JPEG, PNG or WebP). Requires a bearer token.")
            .WithDescription("The image is checked by its content, cropped to a square, resized to 512 px and re-encoded as WebP without metadata. SVG and other formats are rejected.");

        profile.MapDelete("/photo", async Task<Ok<ProfileDto>> (ClaimsPrincipal user, ProfilePhotoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RemoveAsync(user.GetRequiredUserId(), ct)))
            .WithSummary("Remove your profile photo; your initials are shown instead.");

        profile.MapGet("/reader-preferences", async Task<Ok<ReaderPreferencesDto>> (ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetReaderPreferencesAsync(user.GetRequiredUserId(), ct)))
            .WithSummary("Your in-browser reader settings (theme, text size, line spacing, width, page flow).");

        profile.MapPut("/reader-preferences", async Task<Ok<ReaderPreferencesDto>> (ReaderPreferencesDto request, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateReaderPreferencesAsync(user.GetRequiredUserId(), request, ct)))
            .Accepts<ReaderPreferencesDto>(ApiEndpoints.Json)
            .WithSummary("Save your reader settings. fontScale is 80-160 (%), lineHeight 120-220 (hundredths).");
    }

    /// <summary>
    /// Serves profile photos. Anonymous access is allowed so public profiles can show them, but a private
    /// profile's photo is only returned to its owner; for everyone else it does not exist (404).
    /// </summary>
    public static void MapProfilePhotoEndpoint(this RouteGroupBuilder api)
    {
        api.MapGet("/users/{userId:guid}/photo", async Task<Results<FileStreamHttpResult, NotFound>> (Guid userId, ClaimsPrincipal user, HttpContext http, ProfilePhotoService service, CancellationToken ct) =>
            {
                var photo = await service.OpenAsync(userId, user.TryGetUserId(), ct);
                if (photo is null)
                {
                    return TypedResults.NotFound();
                }

                // Private: the response depends on who is asking. The URL carries the version, so a changed
                // photo gets a new URL and short private caching is safe.
                http.Response.Headers.CacheControl = "private, max-age=3600";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return TypedResults.Stream(
                    photo.Content,
                    "image/webp",
                    entityTag: new EntityTagHeaderValue("\"" + photo.Version.ToString(CultureInfo.InvariantCulture) + "\""));
            })
            .AllowAnonymous()
            .WithTags("Profile")
            .WithSummary("A reader's profile photo (WebP). Private profiles' photos are only visible to their owner.");
    }
}

/// <summary>Request body limits for upload endpoints: the configured file limits plus room for multipart framing.</summary>
internal sealed record UploadLimits(long PhotoRequestBytes, long ReadingFileRequestBytes)
{
    private const long Framing = 1024 * 1024;

    public static UploadLimits From(IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Abstractions.UploadOptions>>().Value;
        return new UploadLimits(options.ProfilePhotoMaxBytes + Framing, options.ReadingFileMaxBytes + Framing);
    }
}
