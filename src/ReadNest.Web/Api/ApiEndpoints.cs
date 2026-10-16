using ReadNest.Web.Authorization;
using ReadNest.Web.Configuration;

namespace ReadNest.Web.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapReadNestApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireRateLimiting(RateLimitPolicies.Api);

        api.MapCatalogEndpoints();

        var personal = api.MapGroup(string.Empty)
            .RequireAuthorization(AuthPolicies.ApiUser)
            .AddEndpointFilter<EnsureProfileFilter>();
        personal.MapLibraryEndpoints();
        personal.MapReadingEndpoints();
        personal.MapPersonalEndpoints();
        return app;
    }

    /// <summary>Content type for endpoints that accept JSON request bodies.</summary>
    internal const string Json = "application/json";
}
