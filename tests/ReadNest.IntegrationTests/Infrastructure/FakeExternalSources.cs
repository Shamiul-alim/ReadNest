using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Web.Configuration;

namespace ReadNest.IntegrationTests.Infrastructure;

/// <summary>The identity the fake "Google" provider returns on the next sign-in. No real credentials are involved.</summary>
public sealed class FakeGoogleIdentity
{
    public string Subject { get; set; } = "google-subject-1";

    public string? Email { get; set; } = "someone@gmail.test";

    public string Name { get; set; } = "Google Reader";

    public bool EmailVerified { get; set; } = true;
}

/// <summary>
/// Stands in for Google under the scheme name "Google". A challenge immediately "returns from Google": it signs the
/// configured identity into Identity's external cookie with the challenge properties (login provider, XSRF key)
/// and redirects to the callback, exactly as the real remote handler does after a successful sign-in.
/// </summary>
public sealed class FakeGoogleHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FakeGoogleIdentity identity)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.Subject),
            new(ClaimTypes.Name, identity.Name),
            new(GoogleAuthentication.EmailVerifiedClaim, identity.EmailVerified ? "True" : "False"),
        };
        if (identity.Email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, identity.Email));
        }

        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), properties);
        Response.Redirect(properties.RedirectUri ?? "/");
    }
}

/// <summary>Deterministic reading sources (Google Books access, Open Library, Gutenberg) without network access.</summary>
public sealed class FakeReadingSources : IReadingSourceProvider
{
    public GoogleAccessInfo? Google { get; set; }

    public OpenLibraryAccess? OpenLibrary { get; set; }

    public List<GutenbergBook> Gutenberg { get; } = [];

    public byte[] GutenbergEpub { get; set; } = TestFiles.Epub();

    public bool Unavailable { get; set; }

    public int Downloads { get; private set; }

    public Task<GoogleAccessInfo?> GetGoogleAccessAsync(string? volumeId, string? isbn, CancellationToken cancellationToken) =>
        Unavailable ? throw new ReadingSourceUnavailableException("down") : Task.FromResult(Google);

    public Task<OpenLibraryAccess?> FindOpenLibraryAsync(string? workId, string? isbn, string title, string? author, CancellationToken cancellationToken) =>
        Unavailable ? throw new ReadingSourceUnavailableException("down") : Task.FromResult(OpenLibrary);

    public Task<IReadOnlyList<GutenbergBook>> SearchGutenbergAsync(string title, string? author, CancellationToken cancellationToken) =>
        Unavailable ? throw new ReadingSourceUnavailableException("down") : Task.FromResult<IReadOnlyList<GutenbergBook>>([.. Gutenberg]);

    public Task<GutenbergBook?> GetGutenbergAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(Gutenberg.FirstOrDefault(b => b.Id == id));

    public Task<Stream> OpenGutenbergEpubAsync(GutenbergBook book, CancellationToken cancellationToken)
    {
        Downloads++;
        return Task.FromResult<Stream>(new MemoryStream(GutenbergEpub));
    }
}
