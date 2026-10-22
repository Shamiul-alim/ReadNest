using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using ReadNest.Application;
using ReadNest.Infrastructure;
using ReadNest.Infrastructure.Identity;
using ReadNest.Infrastructure.Persistence;
using ReadNest.Infrastructure.Services;
using ReadNest.Web.Api;
using ReadNest.Web.Authorization;
using ReadNest.Web.Components;
using ReadNest.Web.Components.Account;
using ReadNest.Web.Components.Shared;
using ReadNest.Web.Configuration;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    // Structured (JSON) console logs for log aggregation in deployed environments.
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 5 * 1024 * 1024);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// The default scheme picks the bearer token when one is sent (API clients) and the Identity cookie otherwise
// (browser), so endpoints that allow anonymous access still recognise either kind of signed-in user.
const string cookieOrBearer = "CookieOrBearer";
var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = cookieOrBearer;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authentication.AddPolicyScheme(cookieOrBearer, "Identity cookie or bearer token", options =>
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? IdentityConstants.BearerScheme
            : IdentityConstants.ApplicationScheme);
authentication.AddBearerToken(IdentityConstants.BearerScheme);
authentication.AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "ReadNest.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);

    // API callers get status codes instead of a redirect to the login page.
    options.Events.OnRedirectToLogin = context => RespondOrRedirect(context.HttpContext, context.RedirectUri, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => RespondOrRedirect(context.HttpContext, context.RedirectUri, StatusCodes.Status403Forbidden);
});

builder.Services.AddReadNestAuthorization();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", false);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<ReadNestDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddScoped<ToastService>();

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddReadNestRateLimiting(builder.Configuration.GetValue("RateLimiting:Enabled", true));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHttpLogging(options =>
{
    // Method, path, status and duration only: no headers, cookies, query strings or bodies are logged.
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

if (builder.Configuration.GetValue("ReverseProxy:ForwardHeaders", false))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    await DatabaseInitializer.MigrateAsync(app.Services);
}

if (app.Configuration.GetValue("ReverseProxy:ForwardHeaders", false))
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api => api.UseExceptionHandler());
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/health"),
    web => web.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

if (app.Configuration.GetValue("Https:Redirect", true))
{
    app.UseHttpsRedirection();
}

// Routing must run after UseStatusCodePagesWithReExecute; otherwise the implicit routing at the start of the pipeline
// has already picked "no endpoint" and the re-executed /not-found request comes back as an empty 404.
app.UseRouting();

app.UseSecurityHeaders();
app.UseHttpLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Token endpoints (POST /api/auth/login, /api/auth/register, /api/auth/refresh, ...) for API clients.
app.MapGroup("/api/auth")
    .WithTags("Authentication")
    .RequireRateLimiting(RateLimitPolicies.Auth)
    .MapIdentityApi<ApplicationUser>();

app.MapReadNestApi();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("ReadNest API"));
}

await app.RunAsync();

static Task RespondOrRedirect(HttpContext context, string redirectUri, int statusCode)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = statusCode;
    }
    else
    {
        context.Response.Redirect(redirectUri);
    }

    return Task.CompletedTask;
}

public partial class Program;
