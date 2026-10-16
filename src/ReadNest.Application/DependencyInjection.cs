using Microsoft.Extensions.DependencyInjection;
using ReadNest.Application.Analytics;
using ReadNest.Application.Books;
using ReadNest.Application.Common;
using ReadNest.Application.Goals;
using ReadNest.Application.ImportExport;
using ReadNest.Application.Library;
using ReadNest.Application.Notes;
using ReadNest.Application.Reading;
using ReadNest.Application.Recommendations;
using ReadNest.Application.Reviews;
using ReadNest.Application.Social;

namespace ReadNest.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application services. They are stateless and create a short-lived database context per call,
    /// so they are safe to use from Blazor Server circuits and API requests alike.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<UserClock>();
        services.AddScoped<CatalogService>();
        services.AddScoped<BookCatalogImporter>();
        services.AddScoped<BookService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<CollectionService>();
        services.AddScoped<ShelfService>();
        services.AddScoped<ReadingSessionService>();
        services.AddScoped<NoteService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<GoalService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<ImportExportService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<SocialService>();
        services.AddScoped<ChallengeService>();
        return services;
    }
}
