using Microsoft.Extensions.Caching.Memory;
using ReadNest.Application.Social;
using ReadNest.Web.Authorization;

namespace ReadNest.Web.Api;

/// <summary>
/// Accounts created through the token API (POST /api/auth/register) skip the web registration form, so their
/// profile is created on first authenticated API use. The check is cached to avoid a query per request.
/// </summary>
internal sealed class EnsureProfileFilter(ProfileService profiles, IMemoryCache cache) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.TryGetUserId() is { } userId)
        {
            var key = "profile-ensured:" + userId;
            if (!cache.TryGetValue(key, out _))
            {
                var suggestedName = user.Identity?.Name?.Split('@')[0];
                await profiles.GetOrCreateAsync(userId, suggestedName, null, context.HttpContext.RequestAborted);
                cache.Set(key, true, TimeSpan.FromHours(1));
            }
        }

        return await next(context);
    }
}
