using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReadNest.Application.Abstractions;
using ReadNest.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace ReadNest.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real application against a disposable PostgreSQL container. The schema is created by the EF Core
/// migrations at startup, exactly as in a real deployment. External catalogues are replaced with a fake.
/// </summary>
public sealed class ReadNestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Passw0rd!x";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("readnest_tests")
        .Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public FakeCatalogProvider Catalog { get; } = new();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // Starting the host applies the migrations.
        _ = Services;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString());
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Https:Redirect", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBookCatalogProvider>();
            services.AddSingleton<IBookCatalogProvider>(Catalog);
        });
    }

    /// <summary>Registers a new account through the token API and returns a client authenticated as that user.</summary>
    public async Task<TestUser> CreateUserAsync(string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using var scope = Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return new TestUser(user!.Id, email, client);
    }

    public async Task<T> WithServiceAsync<T, TService>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task WithServiceAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}

public sealed record TestUser(Guid Id, string Email, HttpClient Client);

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<ReadNestFactory>
{
    public const string Name = "database";
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<T>(body, ReadNestFactory.Json)
            ?? throw new InvalidOperationException("Empty response body.");
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ReadNestFactory.Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ReadNestFactory.Json);
}

public static class ResponseExtensions
{
    /// <summary>Returns the body, failing with the status and body when the request was not successful.</summary>
    public static async Task<string> ReadSuccessStringAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return body;
    }
}
