using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Application;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Ledger;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class LedgerTestDb(DbContextOptions<LedgerTestDb> options) : NOFDbContext(options);

public sealed class LedgerPersistenceTests
{
    public static IHostApplicationBuilder Configure(string connectionString)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Clear();
        builder.Services.AddNOFApplication();
        builder.AddNOFEntityFrameworkCore();
        builder.UseDbContext<LedgerTestDb>().WithTenantMode(TenantMode.SharedDatabase)
            .WithConnectionString(connectionString)
            .WithOptions((options, connection) => options.UseSqlite(connection).EnableServiceProviderCaching(false));
        OfficialPlugins.Ledger.LedgerPlugin.ConfigureServices(builder.Services);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLogging();
        return builder;
    }

    [Fact]
    public async Task MigrationSupportsCrudMonthBoundariesAndAtomicIdempotentImports()
    {
        // Match the SQL artifact shipped to Quantum, rather than creating tables from EF.
        // Keep a shared in-memory database alive across NOF's independent connections.
        await using var shared = new SqliteConnection($"Data Source=ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await shared.OpenAsync();
        await using (var migration = shared.CreateCommand())
        {
            migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "001_init.sql"));
            await migration.ExecuteNonQueryAsync();
        }
        var builder = Configure(shared.ConnectionString);
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var service = scope.ServiceProvider.GetRequiredService<LedgerService>();
        await service.SaveAsync(null, new(2026, 10, 1), EntryKind.Expense, 0.1m, "餐饮", "Breakfast");
        await service.SaveAsync(null, new(2026, 10, 31), EntryKind.Income, 100m, "工资", "Salary");
        await service.SaveAsync(null, new(2026, 11, 1), EntryKind.Expense, 50m, "Other", "Next month");
        var month = await service.ListMonthAsync(new(2026, 10, 1));
        Assert.Equal(2, month.Length);
        Assert.Equal(99.9m, LedgerTotals.From(month).Balance);
        var expense = month.Single(item => item.Kind == EntryKind.Expense);
        await service.SaveAsync(expense.Id, new(2026, 10, 2), EntryKind.Expense, 0.2m, "交通", "Changed");
        month = await service.ListMonthAsync(new(2026, 10, 1));
        Assert.Equal(0.2m, month.Single(item => item.Id == expense.Id).Amount);
        Assert.Equal(0, await service.ImportAsync(LedgerCsv.Parse(LedgerCsv.Export(month))));
        var fresh = LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 5), EntryKind.Expense, 12.34m, "New", "");
        var conflict = LedgerEntry.Create(expense.Id, expense.Date, expense.Kind, 999m, expense.Category, "Conflict");
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync([fresh, conflict]));
        Assert.Equal(2, (await service.ListMonthAsync(new(2026, 10, 1))).Length);
        Assert.Equal(1, await service.ImportAsync([fresh]));
        await service.DeleteAsync(fresh.Id);
        Assert.Equal(2, (await service.ListMonthAsync(new(2026, 10, 1))).Length);
        // A new DI scope confirms database persistence and no accidental in-memory store.
        await using var reopened = provider.CreateAsyncScope();
        reopened.ServiceProvider.ResolveDaemonServices();
        var persisted = await reopened.ServiceProvider.GetRequiredService<LedgerService>().ListMonthAsync(new(2026, 11, 1));
        Assert.Equal(50m, Assert.Single(persisted).Amount);
    }
}
