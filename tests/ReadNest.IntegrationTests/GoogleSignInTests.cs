using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadNest.Infrastructure.Identity;
using ReadNest.Infrastructure.Persistence;
using ReadNest.IntegrationTests.Infrastructure;
using static ReadNest.IntegrationTests.Infrastructure.FormExtensions;

namespace ReadNest.IntegrationTests;

/// <summary>
/// "Continue with Google" through Identity external logins. The main test host registers a fake provider under the
/// scheme name "Google" (no real credentials); separate hosts check the real handler's configuration.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GoogleSignInTests(ReadNestFactory factory)
{
    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>Clicks "Continue with Google" on the sign-in page and follows the provider back to the callback page.</summary>
    private static async Task<(HttpResponseMessage Callback, string CallbackUrl)> SignInWithGoogleAsync(HttpClient browser)
    {
        var login = await browser.GetStringAsync("/Account/Login");
        Assert.Contains("Continue with Google", login, StringComparison.Ordinal);

        var challenge = await browser.PostAsync("/Account/PerformExternalLogin", Form(("provider", "Google"), ("ReturnUrl", ""), ("__RequestVerificationToken", AntiforgeryToken(login))));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var callbackUrl = challenge.Headers.Location!.OriginalString;
        Assert.StartsWith("/Account/ExternalLogin", callbackUrl, StringComparison.Ordinal);
        return (await browser.GetAsync(callbackUrl), callbackUrl);
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<IList<UserLoginInfo>> LoginsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetLoginsAsync((await users.FindByIdAsync(userId.ToString()))!);
    }

    private static async Task SignInWithPasswordAsync(HttpClient browser, string email)
    {
        var login = await browser.GetStringAsync("/Account/Login");
        var response = await browser.PostAsync("/Account/Login", Form(
            ("_handler", "login"),
            ("__RequestVerificationToken", AntiforgeryToken(login)),
            ("Input.Email", email),
            ("Input.Password", ReadNestFactory.Password)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("dashboard", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_google_user_confirms_a_name_and_gets_a_linked_account()
    {
        var email = Unique("new") + "@gmail.test";
        factory.GoogleIdentity.Subject = Unique("sub");
        factory.GoogleIdentity.Email = email;
        factory.GoogleIdentity.Name = "Grace Hopper";
        factory.GoogleIdentity.EmailVerified = true;
        var browser = NewBrowser();

        var (callback, callbackUrl) = await SignInWithGoogleAsync(browser);
        var page = await callback.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains("Finish signing up", page, StringComparison.Ordinal);
        Assert.Contains(email, page, StringComparison.Ordinal);
        Assert.Contains("value=\"Grace Hopper\"", page, StringComparison.Ordinal);

        var confirm = await browser.PostAsync(callbackUrl, Form(
            ("_handler", "external-confirmation"),
            ("__RequestVerificationToken", AntiforgeryToken(page)),
            ("Input.DisplayName", "Grace H."),
            ("Input.TimeZone", "Europe/London")));
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        Assert.EndsWith("dashboard", confirm.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var user = await FindUserAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        var login = Assert.Single(await LoginsAsync(user.Id));
        Assert.Equal("Google", login.LoginProvider);
        Assert.Equal(factory.GoogleIdentity.Subject, login.ProviderKey);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReadNestDbContext>();
            var profile = await db.UserProfiles.SingleAsync(p => p.UserId == user.Id);
            Assert.Equal("Grace H.", profile.DisplayName);
            Assert.Equal("Europe/London", profile.TimeZoneId);

            // Google's tokens are never stored.
            Assert.False(await db.UserTokens.AnyAsync(t => t.UserId == user.Id));
        }

        // Signed in: the app opens, and signing out works as for password accounts.
        var dashboard = await browser.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var logout = await browser.PostAsync("/Account/Logout", Form(("ReturnUrl", ""), ("__RequestVerificationToken", AntiforgeryToken(await dashboard.Content.ReadAsStringAsync()))));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var afterLogout = await browser.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        Assert.Contains("Account/Login", afterLogout.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returning_google_user_signs_straight_in()
    {
        var email = Unique("again") + "@gmail.test";
        factory.GoogleIdentity.Subject = Unique("sub");
        factory.GoogleIdentity.Email = email;
        factory.GoogleIdentity.EmailVerified = true;
        var first = NewBrowser();
        var (callback, callbackUrl) = await SignInWithGoogleAsync(first);
        var page = await callback.Content.ReadAsStringAsync();
        (await first.PostAsync(callbackUrl, Form(("_handler", "external-confirmation"), ("__RequestVerificationToken", AntiforgeryToken(page)), ("Input.DisplayName", "Returning")))).EnsureSuccessOrRedirect();

        var (again, _) = await SignInWithGoogleAsync(NewBrowser());

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.EndsWith("dashboard", again.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matching_email_is_never_linked_to_an_existing_account_automatically()
    {
        var existing = await factory.CreateUserAsync(Unique("owner") + "@gmail.test");
        factory.GoogleIdentity.Subject = Unique("attacker");
        factory.GoogleIdentity.Email = existing.Email;
        factory.GoogleIdentity.EmailVerified = true;
        var browser = NewBrowser();

        var (callback, callbackUrl) = await SignInWithGoogleAsync(browser);
        var page = await callback.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains("You already have an account", page, StringComparison.Ordinal);
        Assert.DoesNotContain("external-confirmation", page, StringComparison.Ordinal);
        Assert.Empty(await LoginsAsync(existing.Id));

        // Even a hand-crafted confirmation post is refused (the page has no form, so take a token from another page).
        var token = AntiforgeryToken(await browser.GetStringAsync("/Account/Register"));
        var forged = await browser.PostAsync(callbackUrl, Form(("_handler", "external-confirmation"), ("__RequestVerificationToken", token), ("Input.DisplayName", "Hijack")));
        Assert.NotEqual(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Empty(await LoginsAsync(existing.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/dashboard")).StatusCode);

        // The owner's password sign-in is unaffected.
        await SignInWithPasswordAsync(NewBrowser(), existing.Email);
    }

    [Fact]
    public async Task Signed_in_user_can_explicitly_connect_google()
    {
        var user = await factory.CreateUserAsync();
        factory.GoogleIdentity.Subject = Unique("link");
        factory.GoogleIdentity.Email = Unique("different") + "@gmail.test";
        factory.GoogleIdentity.EmailVerified = true;
        var browser = NewBrowser();
        await SignInWithPasswordAsync(browser, user.Email);

        var manage = await browser.GetStringAsync("/Account/Manage/ExternalLogins");
        Assert.Contains("Connect Google", manage, StringComparison.Ordinal);
        var challenge = await browser.PostAsync("/Account/Manage/LinkExternalLogin", Form(("Provider", "Google"), ("__RequestVerificationToken", AntiforgeryToken(manage))));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var linked = await browser.GetAsync(challenge.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, linked.StatusCode);

        var login = Assert.Single(await LoginsAsync(user.Id));
        Assert.Equal(factory.GoogleIdentity.Subject, login.ProviderKey);

        // From now on, Google signs this account in.
        var (callback, _) = await SignInWithGoogleAsync(NewBrowser());
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.EndsWith("dashboard", callback.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unverified_or_missing_google_email_is_refused()
    {
        factory.GoogleIdentity.Subject = Unique("unverified");
        factory.GoogleIdentity.Email = Unique("u") + "@gmail.test";
        factory.GoogleIdentity.EmailVerified = false;

        var (callback, _) = await SignInWithGoogleAsync(NewBrowser());
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("external=unverified", callback.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Null(await FindUserAsync(factory.GoogleIdentity.Email));

        factory.GoogleIdentity.EmailVerified = true;
        factory.GoogleIdentity.Email = null;
        var (noEmail, _) = await SignInWithGoogleAsync(NewBrowser());
        Assert.Contains("external=no-email", noEmail.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_without_a_pending_sign_in_returns_to_the_login_page()
    {
        var response = await NewBrowser().GetAsync("/Account/ExternalLogin?Action=LoginCallback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("external=expired", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var page = await NewBrowser().GetStringAsync("/Account/Login?external=cancelled");
        Assert.Contains("Google sign-in was cancelled", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_credentials_google_is_not_offered_and_password_sign_in_still_works()
    {
        await using var host = new ConfiguredHostFactory(factory, new Dictionary<string, string?>());
        var browser = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await browser.GetStringAsync("/Account/Login");
        var register = await browser.GetStringAsync("/Account/Register");

        // Outside development the button is hidden entirely (development shows a disabled button with setup help).
        Assert.DoesNotContain("Continue with Google", login, StringComparison.Ordinal);
        Assert.DoesNotContain("Continue with Google", register, StringComparison.Ordinal);
        Assert.False(ReadNest.Web.Configuration.GoogleAuthentication.IsConfigured(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));

        var user = await factory.CreateUserAsync();
        await SignInWithPasswordAsync(browser, user.Email);
    }

    [Fact]
    public async Task With_credentials_the_challenge_goes_to_google_with_the_signin_google_callback()
    {
        await using var host = new ConfiguredHostFactory(factory, new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "test-client-id.apps.googleusercontent.com",
            ["Authentication:Google:ClientSecret"] = "not-a-real-secret",
        });
        var browser = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await browser.GetStringAsync("/Account/Login");
        Assert.Contains("Continue with Google", login, StringComparison.Ordinal);
        Assert.DoesNotContain("google-setup-hint", login, StringComparison.Ordinal);

        var challenge = await browser.PostAsync("/Account/PerformExternalLogin", Form(("provider", "Google"), ("ReturnUrl", ""), ("__RequestVerificationToken", AntiforgeryToken(login))));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!.AbsoluteUri;
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client-id.apps.googleusercontent.com", location, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=" + Uri.EscapeDataString("http://localhost/signin-google"), location, StringComparison.Ordinal);
        Assert.Contains("scope=openid%20profile%20email", location, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-secret", location, StringComparison.Ordinal);

        // A forged or stale callback (bad state) lands back on the sign-in page with a fixed message.
        var bogus = await browser.GetAsync("/signin-google?state=forged&code=abc");
        Assert.Equal(HttpStatusCode.Redirect, bogus.StatusCode);
        Assert.Equal("/Account/Login?external=failed", bogus.Headers.Location!.OriginalString);

        // Cancelling on Google's consent screen.
        var denied = await browser.GetAsync("/signin-google?error=access_denied&state=forged");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/Login?external=", denied.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}

internal static class RedirectAssertions
{
    public static void EnsureSuccessOrRedirect(this HttpResponseMessage response) =>
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect, $"Unexpected status {(int)response.StatusCode}.");
}
