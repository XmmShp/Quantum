using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Ledger;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class LedgerTestDb(DbContextOptions<LedgerTestDb> options) : NOFDbContext(options);

public sealed class LedgerPersistenceTests
{
    public static IHostApplicationBuilder Configure(string connectionString, SaveChangesInterceptor? interceptor = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Clear();
        builder.Services.AddNOFApplication();
        builder.AddNOFEntityFrameworkCore();
        builder.UseDbContext<LedgerTestDb>().WithTenantMode(TenantMode.SharedDatabase)
            .WithConnectionString(connectionString)
            .WithOptions((options, connection) =>
            {
                options.UseSqlite(connection).EnableServiceProviderCaching(false);
                if (interceptor is not null)
                {
                    options.AddInterceptors(interceptor);
                }
            });
        OfficialPlugins.Ledger.LedgerPlugin.ConfigureServices(builder.Services);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLogging();
        return builder;
    }

    [Fact]
    public async Task RelationMigrationPreservesExistingEntries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "001_init.sql"));
        await command.ExecuteNonQueryAsync();
        command.CommandText = "INSERT INTO OfficialLedgerEntries (Id, Date, Kind, AmountInFen, Category, Note, TenantId) VALUES ('old', '2026-09-01', 0, 1234, 'Food', 'Existing', 'tenant')";
        await command.ExecuteNonQueryAsync();
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "002_entry_relations.sql"));
        await command.ExecuteNonQueryAsync();
        command.CommandText = "INSERT INTO OfficialLedgerEntries (Id, Date, Kind, AmountInFen, Category, Note, TenantId, RelatedExpenseId) VALUES ('linked', '2026-10-01', 1, 100, 'Repayment', '', 'tenant', 'old')";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "SELECT RelatedExpenseId FROM OfficialLedgerEntries WHERE Id = 'linked'";
        Assert.Equal("old", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT AmountInFen, Note, RelatedExpenseId FROM OfficialLedgerEntries WHERE Id = 'old'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1234, reader.GetInt64(0));
        Assert.Equal("Existing", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal(3, reader.FieldCount);
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
            migration.CommandText += await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "002_entry_relations.sql"));
            await migration.ExecuteNonQueryAsync();
        }
        var scopes = new SaveScopes();
        var builder = Configure(shared.ConnectionString, scopes);
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var client = scope.ServiceProvider.GetRequiredService<Quantum.OfficialPlugins.Ledger.Rpc.ILedgerRpcServiceClient>();
        var context = NOF.Contract.Context.Empty;
        var expense = await client.CreateEntryAsync(new(new(2026, 10, 1), EntryKind.Expense, .1m, "Food"), context);
        Assert.True(expense.IsSuccess, expense.Message);
        var id = expense.Value!.Id;
        var income = await client.CreateEntryAsync(new(new(2026, 11, 1), EntryKind.Income, .1m, "Repayment", "", id), context);
        Assert.True(income.IsSuccess, income.Message);
        Assert.Equal(id, income.Value!.RelatedExpenseId);
        Assert.Single((await client.GetRelatedEntriesAsync(new(id), context)).Value!);
        var blocked = await client.DeleteEntryAsync(new(id), context);
        Assert.False(blocked.IsSuccess);
        Assert.True((await client.UpdateEntryAsync(new(id, new(new(2026, 10, 2), EntryKind.Expense, .2m, "Transport")), context)).IsSuccess);
        var exported = await client.ExportCsvAsync(new(new(2026, 10, 1)), context);
        Assert.True(exported.IsSuccess, exported.Message);
        Assert.Equal(0, (await client.ImportCsvAsync(new(exported.Value!.Csv), context)).Value!.AddedCount);
        var fresh = LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 5), EntryKind.Expense, 12.34m, "New", "");
        var conflict = LedgerEntry.Create(id, new(2026, 10, 2), EntryKind.Expense, 999m, "Transport", "Conflict");
        Assert.False((await client.ImportCsvAsync(new(LedgerCsv.Export([fresh, conflict])), context)).IsSuccess);
        Assert.Single((await client.ListEntriesAsync(new(new(2026, 10, 1)), context)).Value!);
        Assert.Equal(1, (await client.ImportCsvAsync(new(LedgerCsv.Export([fresh])), context)).Value!.AddedCount);
        Assert.True((await client.DeleteEntryAsync(new(fresh.Id), context)).IsSuccess);
        var invalid = await client.UpdateEntryAsync(new(id, new(new(2026, 10, 2), EntryKind.Expense, 0m, "Bad")), context);
        Assert.False(invalid.IsSuccess);
        // The same long-lived client can recover after failed requests; each dispatch owns a fresh scope.
        Assert.Equal(.2m, (await client.GetEntryAsync(new(id), context)).Value!.Entry!.Amount);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateEntryAsync(
            new(new(2026, 10, 1), EntryKind.Expense, 1m, "Cancelled"), context, cancelled.Token));
        Assert.Single((await client.ListEntriesAsync(new(new(2026, 10, 1)), context)).Value!);
        await using var reopened = provider.CreateAsyncScope();
        reopened.ServiceProvider.ResolveDaemonServices();
        var reopenedClient = reopened.ServiceProvider.GetRequiredService<Quantum.OfficialPlugins.Ledger.Rpc.ILedgerRpcServiceClient>();
        Assert.Equal(.1m, Assert.Single((await reopenedClient.ListEntriesAsync(new(new(2026, 11, 1)), context)).Value!).Amount);
        Assert.True(scopes.ContextIds.Count >= 5);
        Assert.Equal(scopes.ContextIds.Count, scopes.ContextIds.Distinct().Count());
    }

    private sealed class SaveScopes : SaveChangesInterceptor
    {
        public List<Guid> ContextIds { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ContextIds.Add(eventData.Context!.ContextId.InstanceId);
            return ValueTask.FromResult(result);
        }
    }
}
