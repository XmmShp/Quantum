using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;

namespace Quantum.Plugins.Persistence;

internal static class PluginPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddQuantumPluginPersistence(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        InitializeSqliteFactory();

        var fullDatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullDatabasePath)
            ?? throw new ArgumentException("Database path must have a parent directory.", nameof(databasePath));
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullDatabasePath,
            Pooling = false
        }.ToString();

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Clear();
        builder.Services.AddNOFApplication();
        builder.AddNOFEntityFrameworkCore();
        builder.UseDbContext<QuantumPluginDbContext>()
            .WithTenantMode(TenantMode.SharedDatabase)
            .WithConnectionString(connectionString)
            .WithOptions(static (options, connectionString) => options
                .UseSqlite(connectionString)
                .EnableServiceProviderCaching(false));

        foreach (var descriptor in builder.Services)
        {
            services.Add(descriptor);
        }

        services.AddLogging();
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<CurrentTenant>());
        services.AddScoped<IMutableCurrentTenant>(provider => provider.GetRequiredService<CurrentTenant>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PluginDatabaseInitializer>();
        return services;
    }

    private static void InitializeSqliteFactory()
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            SqliteConnection.ClearAllPools();
            return;
        }

        using var flow = ExecutionContext.SuppressFlow();
        SqliteConnection.ClearAllPools();
    }
}
