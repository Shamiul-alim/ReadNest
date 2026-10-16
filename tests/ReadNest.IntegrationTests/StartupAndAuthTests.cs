using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadNest.Infrastructure.Persistence;
using ReadNest.IntegrationTests.Infrastructure;

namespace ReadNest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class StartupAndAuthTests(ReadNestFactory factory)
{
    [Fact]
    public async Task Health_endpoints_report_healthy_with_database()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Migrations_are_applied_and_match_the_model()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadNestDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The EF model has changes that are not captured in a migration.");
        Assert.True(await db.Genres.AnyAsync(g => g.Name == "Software Engineering"), "Seeded genres are missing.");
    }

    [Fact]
    public async Task Landing_and_login_pages_render_with_security_headers()
    {
        var client = factory.CreateClient();

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("A calm home for every book you read.", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("nosniff", home.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", home.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(home.Headers.Contains("Content-Security-Policy"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Register")).StatusCode);
    }

    [Fact]
    public async Task Protected_pages_redirect_anonymous_users_to_login()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/library")]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/notes")]
    [InlineData("/api/v1/export/library.json")]
    [InlineData("/api/v1/goals")]
    public async Task Private_api_endpoints_return_401_without_credentials(string url)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Invalid_bearer_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/library")).StatusCode);
    }

    [Fact]
    public async Task Openapi_document_is_not_published_outside_development()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Registered_user_gets_a_profile_on_first_api_call()
    {
        var user = await factory.CreateUserAsync();

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/v1/library")).StatusCode);
        var profile = await factory.WithServiceAsync<ReadNest.Application.Social.ProfileDto?, ReadNest.Application.Social.ProfileService>(
            s => s.GetAsync(user.Id, CancellationToken.None));
        Assert.NotNull(profile);
        Assert.False(profile.IsProfilePublic);
    }
}
