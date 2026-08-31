using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;

namespace Quantum.ExtensionMarket.Infrastructure;

public static class PostgreSqlHostBuilderExtensions
{
    public static IHostApplicationBuilder AddExtensionMarketPostgreSql(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'postgres' is required.");
        }

        builder.UseDbContext<ExtensionMarketDbContext>()
            .WithTenantMode(TenantMode.DatabasePerTenant)
            .WithConnectionString(connectionString)
            .WithOptions(static (options, configuredConnectionString) =>
                options.UseNpgsql(configuredConnectionString))
            .MigrateOnInitialize();
        return builder;
    }
}
