using System.Net;
using System.Threading.RateLimiting;
using ReadNest.Web.Authorization;

namespace ReadNest.Web.Configuration;

public static class RateLimitPolicies
{
    public const string Api = "api";
    public const string Catalog = "catalog";
    public const string Auth = "auth";
    public const string Import = "import";

    /// <param name="enabled">Set <c>RateLimiting:Enabled=false</c> to turn limits off (automated tests only).</param>
    public static IServiceCollection AddReadNestRateLimiting(this IServiceCollection services, bool enabled = true)
    {
        services.AddRateLimiter(options =>
        {
            if (!enabled)
            {
                foreach (var name in new[] { Api, Catalog, Auth, Import })
                {
                    options.AddPolicy(name, _ => RateLimitPartition.GetNoLimiter("disabled"));
                }

                return;
            }

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(Api, context => FixedWindow(context, 300, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Catalog, context => FixedWindow(context, 40, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Auth, context => FixedWindow(context, 10, TimeSpan.FromMinutes(1), byIpOnly: true));
            options.AddPolicy(Import, context => FixedWindow(context, 5, TimeSpan.FromMinutes(10)));

            // Brute-force protection for the Identity form posts (login, register, password reset ...).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/Account")
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        "account:" + ClientIp(context),
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("none"));
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permits, TimeSpan window, bool byIpOnly = false)
    {
        var key = !byIpOnly && context.User.TryGetUserId() is { } userId ? "user:" + userId : "ip:" + ClientIp(context);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
    }

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
}

public static class SecurityHeaders
{
    /// <summary>
    /// Conservative security headers. The CSP allows inline scripts because Blazor emits an inline import map;
    /// tightening it with nonces is listed as a deployment hardening task in the README.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (!context.Request.Path.StartsWithSegments("/scalar"))
            {
                headers.ContentSecurityPolicy =
                    "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; " +
                    "script-src 'self' 'unsafe-inline'; connect-src 'self'; font-src 'self'; object-src 'none'; " +
                    "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            }

            await next();
        });
}
