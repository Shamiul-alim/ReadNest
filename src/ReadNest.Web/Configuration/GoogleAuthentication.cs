using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace ReadNest.Web.Configuration;

/// <summary>
/// "Continue with Google" through ASP.NET Core Identity external logins. The handler is only registered when both
/// <c>Authentication:Google:ClientId</c> and <c>Authentication:Google:ClientSecret</c> are configured (user secrets
/// in development, environment variables in production); without them the app runs normally with password sign-in.
/// </summary>
public static class GoogleAuthentication
{
    public const string Scheme = GoogleDefaults.AuthenticationScheme;

    /// <summary>Path Google redirects back to. Register <c>{origin}/signin-google</c> as an authorised redirect URI.</summary>
    public const string CallbackPath = "/signin-google";

    /// <summary>Claim carrying Google's "email_verified" flag.</summary>
    public const string EmailVerifiedClaim = "urn:google:email_verified";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

    public static AuthenticationBuilder AddGoogleIfConfigured(this AuthenticationBuilder authentication, IConfiguration configuration)
    {
        if (!IsConfigured(configuration))
        {
            return authentication;
        }

        return authentication.AddGoogle(Scheme, "Google", options =>
        {
            options.ClientId = configuration["Authentication:Google:ClientId"]!;
            options.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
            options.CallbackPath = CallbackPath;
            options.SignInScheme = IdentityConstants.ExternalScheme;

            // Only the identity is needed; Google's tokens are never stored.
            options.SaveTokens = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "verified_email");
            options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");

            // Google returns with a top-level GET, so Lax is enough and also works on http://localhost.
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            // Cancelling on Google's consent screen, or any protocol failure, returns to the sign-in page with a
            // fixed message instead of an error page. Nothing from the provider is echoed back.
            options.Events.OnAccessDenied = context =>
            {
                context.Response.Redirect(FailureTarget(context.Request, context.Properties, "cancelled"));
                context.HandleResponse();
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GoogleAuthentication));
                logger.LogWarning("Google sign-in failed: {Reason}", context.Failure?.Message);
                context.Response.Redirect(FailureTarget(context.Request, context.Properties, "failed"));
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>Back to the page that started the flow: account settings when connecting, otherwise sign-in.</summary>
    private static string FailureTarget(HttpRequest request, AuthenticationProperties? properties, string reason)
    {
        var connecting = properties?.RedirectUri?.Contains("/Account/Manage/ExternalLogins", StringComparison.OrdinalIgnoreCase) == true;
        return request.PathBase + (connecting ? "/Account/Manage/ExternalLogins" : "/Account/Login") + "?external=" + reason;
    }
}
