using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.Platform.Persistence;

namespace Quantum.Platform;

public static class PostgreSqlHostBuilderExtensions
{
    public static IHostApplicationBuilder AddQuantumPlatformPostgreSql(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'postgres' is required.");
        }

        builder.UseDbContext<PlatformDbContext>()
            .WithTenantMode(TenantMode.DatabasePerTenant)
            .WithConnectionString(connectionString)
            .WithOptions(static (options, configuredConnectionString) =>
                options.UseNpgsql(configuredConnectionString))
            .MigrateOnInitialize();
        return builder;
    }
}
