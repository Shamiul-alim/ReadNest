using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
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
/// migrations at startup, exactly as in a real deployment. External catalogues and reading sources are replaced with
/// fakes, uploads go to a temporary folder that is deleted afterwards, and "Google" is a fake external provider.
/// The developer's own database and storage are never touched.
/// </summary>
public sealed class ReadNestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Passw0rd!x";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("readnest_tests")
        .Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public FakeCatalogProvider Catalog { get; } = new();

    public FakeReadingSources ReadingSources { get; } = new();

    public FakeGoogleIdentity GoogleIdentity { get; } = new();

    /// <summary>Upload limit used by the tests (smaller than the 5 MB / 50 MB defaults, to keep tests fast).</summary>
    public const long UploadLimit = 1024 * 1024;

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "readnest-tests-" + Guid.NewGuid().ToString("N"));

    public string ConnectionString => _database.GetConnectionString();

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
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString());
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Https:Redirect", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Storage:RootPath", StorageRoot);
        builder.UseSetting("Uploads:ProfilePhotoMaxBytes", UploadLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Uploads:ReadingFileMaxBytes", UploadLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBookCatalogProvider>();
            services.AddSingleton<IBookCatalogProvider>(Catalog);
            services.RemoveAll<IReadingSourceProvider>();
            services.AddSingleton<IReadingSourceProvider>(ReadingSources);
            services.AddSingleton(GoogleIdentity);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeGoogleHandler>("Google", "Google", _ => { });
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

/// <summary>
/// A second host on the same test database with different configuration (for example real Google credentials that
/// are dummies, or none at all). It has no fake "Google" provider.
/// </summary>
public sealed class ConfiguredHostFactory(ReadNestFactory shared, IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", shared.ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("Https:Redirect", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Storage:RootPath", shared.StorageRoot);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBookCatalogProvider>();
            services.AddSingleton<IBookCatalogProvider>(shared.Catalog);
            services.RemoveAll<IReadingSourceProvider>();
            services.AddSingleton<IReadingSourceProvider>(shared.ReadingSources);
        });
    }
}

public static class FormExtensions
{
    /// <summary>The first antiforgery request token on an HTML page.</summary>
    public static string AntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : throw new InvalidOperationException("No antiforgery token on the page.");
    }

    public static FormUrlEncodedContent Form(params (string Name, string? Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value ?? string.Empty)));
}

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
