using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace ReadNest.Web.Authorization;

public static class AuthPolicies
{
    /// <summary>Authenticated via the Identity cookie (browser) or an Identity bearer token (API clients).</summary>
    public const string ApiUser = "ApiUser";

    /// <summary>Bearer token only; used for state-changing multipart endpoints that cannot carry an antiforgery token.</summary>
    public const string BearerOnly = "BearerOnly";

    public static IServiceCollection AddReadNestAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ApiUser, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(BearerOnly, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser());
        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The user id from the validated authentication ticket. This is the only source of identity used to scope
    /// data access; ids supplied in URLs or request bodies are never trusted for ownership.
    /// </summary>
    public static Guid? TryGetUserId(this ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public static Guid GetRequiredUserId(this ClaimsPrincipal? user) =>
        user.TryGetUserId() ?? throw new UnauthorizedAccessException("Authentication is required.");
}
