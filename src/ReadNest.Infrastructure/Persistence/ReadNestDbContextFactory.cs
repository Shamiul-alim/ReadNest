using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using ReadNest.Application.Abstractions;

namespace ReadNest.Infrastructure.Persistence;

internal sealed class ReadNestDbContextFactory(IDbContextFactory<ReadNestDbContext> factory) : IReadNestDbContextFactory
{
    public async Task<IReadNestDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken);
}

/// <summary>
/// Used only by <c>dotnet ef</c> tooling. Reads the connection string from the
/// <c>ConnectionStrings__DefaultConnection</c> environment variable, falling back to the local Docker database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReadNestDbContext>
{
    public ReadNestDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=readnest;Username=readnest;Password=readnest_dev_password";

        // Identity reads its schema version from IdentityOptions; it must match the application's (v3 = passkeys).
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        var options = new DbContextOptionsBuilder<ReadNestDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ReadNestDbContext).Assembly.FullName))
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .Options;
        return new ReadNestDbContext(options);
    }
}
