using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NOF.Contract;
using Quantum.OfficialPlugins.Calendar;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Rpc;
using System.Data.Common;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class ProjectionQueryTests
{
    [Fact]
    public async Task CalendarProjectsOnlyDtoColumnsAndMatchesTheInMemoryMapping()
    {
        await using var connection = new SqliteConnection($"Data Source=calendar-projection-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "calendar-migrations"), "*.sql").Order(StringComparer.Ordinal))
        {
            await using var migration = connection.CreateCommand();
            migration.CommandText = await File.ReadAllTextAsync(file);
            await migration.ExecuteNonQueryAsync();
        }
        var commands = new QueryCommands();
        var builder = LedgerPersistenceTests.Configure(connection.ConnectionString, queryInterceptor: commands);
        CalendarPluginInitializer.Initialize(builder.Services);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.calendar", typeof(ICalendarRpcService).Assembly);
        await host.UseClient<ICalendarRpcServiceClient>(async client =>
        {
            var created = await client.CreateEntryAsync(new()
            {
                Title = "Projected task",
                Notes = "",
                Kind = CalendarEntryKind.Task,
                Date = new(2026, 10, 1),
                StartTime = new(9, 0),
                EndTime = null,
                Style = "blue"
            }, Context.Empty);
            Assert.True(created.IsSuccess, created.Message);
            commands.Sql.Clear();

            var result = await client.ListEntriesAsync(new()
            {
                StartDate = new(2026, 10, 1),
                EndDate = new(2026, 10, 2),
                Kind = CalendarEntryKind.Task
            }, Context.Empty);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(created.Value, Assert.Single(result.Value!));
            var select = Assert.Single(commands.Sql);
            Assert.DoesNotContain("\"CreatedAt\"", select);
            Assert.DoesNotContain("\"UpdatedAt\"", select);
        });
    }

    [Fact]
    public async Task LedgerProjectsMoneyInSqlAndCompletesLinksAfterMaterialization()
    {
        await using var connection = new SqliteConnection($"Data Source=ledger-projection-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await LedgerPersistenceTests.Migrate(connection);
        var commands = new QueryCommands();
        var builder = LedgerPersistenceTests.Configure(connection.ConnectionString, queryInterceptor: commands);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.ledger", typeof(ILedgerRpcService).Assembly);
        var eventId = Guid.NewGuid();
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            var first = await client.CreateEntryAsync(new()
            {
                Date = new(2026, 10, 1),
                Kind = EntryKind.Expense,
                Amount = 123.45m,
                Category = "Trip",
                CalendarEntryIds = [eventId]
            }, Context.Empty);
            Assert.True(first.IsSuccess, first.Message);
            var second = await client.CreateEntryAsync(new()
            {
                Date = new(2026, 10, 2),
                Kind = EntryKind.Income,
                Amount = 0.01m,
                Category = "Refund",
                RelatedEntryIds = [first.Value!.Id]
            }, Context.Empty);
            Assert.True(second.IsSuccess, second.Message);
            commands.Sql.Clear();

            var result = await client.ListEntriesAsync(new()
            {
                Month = new(2026, 10, 1)
            }, Context.Empty);

            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal(new[] { second.Value!.Id, first.Value.Id }, result.Value!.Select(entry => entry.Id));
            var expense = Assert.Single(result.Value, entry => entry.Id == first.Value.Id);
            Assert.Equal(123.45m, expense.Amount);
            Assert.Equal(new[] { eventId }, expense.CalendarEntryIds);
            Assert.Equal(new[] { second.Value.Id }, expense.RelatedEntryIds);
            Assert.Equal(0.01m, result.Value[0].Amount);
            var select = Assert.Single(commands.Sql, sql => sql.Contains("FROM \"OfficialLedgerEntries\"", StringComparison.Ordinal));
            Assert.Contains("ef_divide", select);
            Assert.Equal(2, commands.Sql.Count);
        });
    }

    private sealed class QueryCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
