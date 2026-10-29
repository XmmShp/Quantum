using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.Platform.Persistence;

namespace Quantum.Platform.Tests;

public sealed class LoginSessionMigrationTests
{
    [Fact]
    public void SessionMigrationMatchesTheRealNofModelAndIsAdditive()
    {
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.AddOidcServer(options => options.Issuer = "https://platform.example/oauth2");
        builder.UseDbContext<PlatformDbContext>().WithTenantMode(TenantMode.DatabasePerTenant)
            .WithConnectionString("Host=localhost;Database=model_only;Username=postgres")
            .WithOptions(static (options, connection) => options.UseNpgsql(connection));
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
        using var db = factory.CreateDbContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261019000000_AddMobileBridge", "20261024000000_AddPersistentLoginSessions");
        Assert.Contains("CREATE TABLE \"PlatformLoginSession\"", sql);
        Assert.Contains("\"TokenHash\" character varying(64)", sql);
        Assert.Contains("\"UserId\" bigint", sql);
        Assert.Contains("\"__DeletedAtUnixTime\"", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("ALTER TABLE", sql);
    }
}
