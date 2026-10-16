using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReadNest.Infrastructure.Persistence;

namespace ReadNest.Infrastructure.Services;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending EF Core migrations. Migrations are the only schema source of truth (no EnsureCreated).
    /// Enabled by <c>Database:ApplyMigrationsOnStartup</c> (on in Development); production deployments can instead
    /// run migrations as a separate step (see README).
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ReadNestDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        logger.LogInformation("Applying database migrations (if any are pending)");
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database schema is up to date");
    }
}
