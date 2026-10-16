using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using ReadNest.Application.Abstractions;
using ReadNest.Application.Books;
using ReadNest.Application.Reviews;
using ReadNest.Infrastructure.ExternalBooks;
using ReadNest.Infrastructure.Persistence;

namespace ReadNest.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. Set ConnectionStrings__DefaultConnection or use 'dotnet user-secrets'. See README.md.");
        }

        // Application services create one short-lived context per operation; Identity uses a scoped context.
        services.AddDbContextFactory<ReadNestDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ReadNestDbContext).Assembly.FullName)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ReadNestDbContext>>().CreateDbContext());
        services.AddSingleton<IReadNestDbContextFactory, ReadNestDbContextFactory>();

        services.AddMemoryCache(options => options.SizeLimit = null);
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.Configure<FeatureOptions>(configuration.GetSection(FeatureOptions.SectionName));
        services.Configure<GoogleBooksOptions>(configuration.GetSection(GoogleBooksOptions.SectionName));
        services.Configure<OpenLibraryOptions>(configuration.GetSection(OpenLibraryOptions.SectionName));

        services.AddHttpClient<GoogleBooksProvider>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<GoogleBooksOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<OpenLibraryProvider>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var contact = string.IsNullOrWhiteSpace(options.ContactEmail) ? "+https://github.com/readnest" : options.ContactEmail;
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"ReadNest/1.0 ({contact})");
            })
            .AddStandardResilienceHandler(ConfigureResilience);

        services.AddTransient<IBookCatalogProvider>(sp => sp.GetRequiredService<GoogleBooksProvider>());
        services.AddTransient<IBookCatalogProvider>(sp => sp.GetRequiredService<OpenLibraryProvider>());

        services.AddHealthChecks().AddDbContextCheck<ReadNestDbContext>("database", tags: ["ready"]);
        return services;
    }

    /// <summary>Short timeouts and few retries: a slow catalogue should fail fast and fall back to the next provider.</summary>
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.Retry.MaxRetryAttempts = 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(400);

        // Retry transient failures only. A 429 means "slow down": retrying it would ignore the provider's limits,
        // so the catalogue service falls back to the next provider instead.
        options.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
        {
            { Exception: HttpRequestException or Polly.Timeout.TimeoutRejectedException } => true,
            { Result: { } response } => response.StatusCode is System.Net.HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500,
            _ => false,
        });
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }
}
