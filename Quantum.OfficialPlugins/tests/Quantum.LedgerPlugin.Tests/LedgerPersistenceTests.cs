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

public sealed class LedgerTestDb : NOFDbContext
{
    public LedgerTestDb(DbContextOptions<LedgerTestDb> options) : base(options)
    {
    }
}

public sealed class LedgerPersistenceTests
{
    public static IHostApplicationBuilder Configure(string connectionString, SaveChangesInterceptor? interceptor = null, DbCommandInterceptor? queryInterceptor = null)
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
                if (queryInterceptor is not null)
                {
                    options.AddInterceptors(queryInterceptor);
                }
            });
        Quantum.Plugins.PluginRpcServiceCollectionExtensions.AddQuantumPluginRpc(builder.Services);
        foreach (var initializer in System.Reflection.CustomAttributeExtensions.GetCustomAttributes<NOF.Abstraction.AssemblyInitializeAttribute>(typeof(OfficialPlugins.Ledger.LedgerPlugin).Assembly))
        {
            initializer.Initialize(builder.Services);
        }
        OfficialPlugins.Ledger.LedgerPlugin.ConfigureServices(builder.Services);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLogging();
        return builder;
    }

    internal static async Task Migrate(SqliteConnection connection, int count = 4)
    {
        foreach (var file in new[] { "001_init.sql", "002_entry_relations.sql", "003_entry_clues.sql", "004_calendar_links.sql" }.Take(count))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ClueMigrationPreservesLegacyRecordsAndBothDirectionsAcrossMonths()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await Migrate(connection, 2);
        await using var command = connection.CreateCommand();
        var original = Guid.NewGuid().ToString().ToUpperInvariant();
        var repayment = Guid.NewGuid().ToString().ToUpperInvariant();
        command.CommandText = "INSERT INTO OfficialLedgerEntries (Id, Date, Kind, AmountInFen, Category, Note, TenantId, RelatedExpenseId) " +
            "VALUES ($original, '2026-09-01', 0, 1234, 'Food', 'Existing', 'tenant', NULL), " +
            "($repayment, '2026-10-01', 1, 100, 'Repayment', 'Legacy', 'tenant', $original)";
        command.Parameters.AddWithValue("$original", original);
        command.Parameters.AddWithValue("$repayment", repayment);
        await command.ExecuteNonQueryAsync();
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "003_entry_clues.sql"));
        await command.ExecuteNonQueryAsync();
        command.CommandText = "SELECT COUNT(*) FROM OfficialLedgerEntries";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT AmountInFen FROM OfficialLedgerEntries WHERE Id = $original";
        Assert.Equal(1234L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT LeftEntryId, RightEntryId, TenantId FROM OfficialLedgerEntryLinks";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new[] { original, repayment }.Order(StringComparer.Ordinal), new[] { reader.GetString(0), reader.GetString(1) });
        Assert.Equal("tenant", reader.GetString(2));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task LocalClientPersistsCluesRecoversAfterFailureAndUsesFreshRequestScopes()
    {
        await using var connection = new SqliteConnection($"Data Source=ledger-local-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await Migrate(connection);
        var scopes = new SaveScopes();
        var builder = Configure(connection.ConnectionString, scopes);
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var client = scope.ServiceProvider.GetRequiredService<OfficialPlugins.Ledger.Rpc.ILedgerRpcServiceClient>();
        var context = NOF.Contract.Context.Empty;
        var first = (await client.CreateEntryAsync(new()
        {
            Date = new(2026, 10, 1),
            Kind = EntryKind.Expense,
            Amount = .1m,
            Category = "Food"
        }, context)).Value!;
        var second = await client.CreateEntryAsync(new()
        {
            Date = new(2026, 11, 1),
            Kind = EntryKind.Expense,
            Amount = .2m,
            Category = "Transport",
            Note = "",
            RelatedEntryIds = [first.Id]
        }, context);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(first.Id, Assert.Single(second.Value!.RelatedEntryIds));
        Assert.Equal(second.Value.Id, Assert.Single((await client.GetRelatedEntriesAsync(new()
        {
            Id = first.Id
        }, context)).Value!).Id);
        var invalid = await client.UpdateEntryAsync(new()
        {
            Id = first.Id,
            Entry = new()
            {
                Date = new(2026, 10, 2),
                Kind = EntryKind.Income,
                Amount = 5m,
                Category = "Bad",
                Note = "",
                RelatedEntryIds = [Guid.NewGuid()]
            }
        }, context);
        Assert.False(invalid.IsSuccess);
        var unchanged = (await client.GetEntryAsync(new()
        {
            Id = first.Id
        }, context)).Value!.Entry!;
        Assert.Equal(.1m, unchanged.Amount);
        Assert.Equal(EntryKind.Expense, unchanged.Kind);
        Assert.Equal(second.Value.Id, Assert.Single(unchanged.RelatedEntryIds));
        var changed = await client.UpdateEntryAsync(new()
        {
            Id = first.Id,
            Entry = new()
            {
                Date = new(2026, 10, 2),
                Kind = EntryKind.Income,
                Amount = .3m,
                Category = "Changed",
                Note = "",
                RelatedEntryIds = [second.Value.Id]
            }
        }, context);
        Assert.True(changed.IsSuccess, changed.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateEntryAsync(
            new()
            {
                Date = new(2026, 10, 1),
                Kind = EntryKind.Expense,
                Amount = 1m,
                Category = "Cancelled"
            }, context, cancelled.Token));
        Assert.Single((await client.ListEntriesAsync(new()
        {
            Month = new(2026, 10, 1)
        }, context)).Value!);
        await using var reopened = provider.CreateAsyncScope();
        reopened.ServiceProvider.ResolveDaemonServices();
        var reopenedClient = reopened.ServiceProvider.GetRequiredService<OfficialPlugins.Ledger.Rpc.ILedgerRpcServiceClient>();
        Assert.Equal(.3m, (await reopenedClient.GetEntryAsync(new()
        {
            Id = first.Id
        }, context)).Value!.Entry!.Amount);
        Assert.True((await client.DeleteEntryAsync(new()
        {
            Id = first.Id
        }, context)).IsSuccess);
        var survivor = (await reopenedClient.GetEntryAsync(new()
        {
            Id = second.Value.Id
        }, context)).Value!.Entry!;
        Assert.Equal(.2m, survivor.Amount);
        Assert.Empty(survivor.RelatedEntryIds);
        Assert.True(scopes.ContextIds.Count >= 4);
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
