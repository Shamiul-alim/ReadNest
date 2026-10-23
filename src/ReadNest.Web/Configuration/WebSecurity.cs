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
    // blob:/data: sources are for the in-browser readers: PDF.js renders fonts from data, and epub.js shows a
    // book's images, styles and fonts from blob URLs inside a sandboxed iframe (no scripts) on ReadNest's origin.
    private const string DefaultPolicy =
        "default-src 'self'; img-src 'self' https: data: blob:; style-src 'self' 'unsafe-inline' blob:; " +
        "script-src 'self' 'unsafe-inline'; connect-src 'self'; font-src 'self' data: blob:; worker-src 'self' blob:; " +
        "frame-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // The Google Books embedded viewer page loads Google's viewer script, which then loads more script and a frame
    // from Google's regional domains (books.google.com redirects to books.google.<country>), so this page alone allows
    // https sources. It shows no user content apart from an HTML-encoded title, and keeps object-src, base-uri,
    // form-action and frame-ancestors locked down.
    private const string GoogleViewerPolicy =
        "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline' https:; " +
        "script-src 'self' 'unsafe-inline' https:; connect-src 'self' https:; font-src 'self' https:; " +
        "frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

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
                headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/reader/google") ? GoogleViewerPolicy : DefaultPolicy;
            }

            await next();
        });
}
