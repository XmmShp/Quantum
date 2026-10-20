using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NOF.Contract;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Rpc;
using System.Reflection;
using System.Text.Json;
using Xunit;
using CalendarPage = Quantum.OfficialPlugins.Calendar.Pages.Calendar;
using LedgerPage = Quantum.OfficialPlugins.Ledger.Pages.Ledger;

namespace Quantum.LedgerPlugin.Tests;

public sealed class CalendarLedgerIntegrationTests
{
    [Fact]
    public async Task LinksPersistAcrossMonthsValidateAtomicallyAndWorkWithoutCalendar()
    {
        await using var connection = new SqliteConnection($"Data Source=event-links-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await LedgerPersistenceTests.Migrate(connection);
        var builder = LedgerPersistenceTests.Configure(connection.ConnectionString);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.ledger", typeof(ILedgerRpcService).Assembly);
        var eventId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        Guid id = default;
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            var saved = await client.CreateEntryAsync(new(new(2026, 9, 1), EntryKind.Expense, 12m, "Food", CalendarEntryIds: [eventId, taskId, eventId]), Context.Empty);
            Assert.True(saved.IsSuccess);
            id = saved.Value!.Id;
            Assert.Equal(2, saved.Value.CalendarEntryIds!.Length);
            var invalid = await client.UpdateEntryAsync(new(id, new(new(2026, 10, 1), EntryKind.Income, 30m, "Changed", CalendarEntryIds: [Guid.Empty])), Context.Empty);
            Assert.False(invalid.IsSuccess);
            var unchanged = (await client.GetEntryAsync(new(id), Context.Empty)).Value!.Entry!;
            Assert.Equal(12m, unchanged.Amount);
            Assert.Equal(2, unchanged.CalendarEntryIds!.Length);
        });
        var found = OfficialRpcHarness.Value(await host.Call("quantum.plugin.ledger.ledger.by-calendar-entry", new
        {
            calendarEntryId = eventId
        }));
        Assert.Equal(id, Assert.Single(found.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(2, found[0].GetProperty("calendarEntryIds").GetArrayLength());
        Assert.Empty(OfficialRpcHarness.Value(await host.Call("ledger.by-calendar-entry", new
        {
            calendarEntryId = Guid.NewGuid()
        })).EnumerateArray());
        var bad = await host.Call("ledger.by-calendar-entry", new
        {
            calendarEntryId = Guid.Empty
        });
        Assert.False(bad.GetProperty("isSuccess").GetBoolean());
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            // Old clients omitting the optional field preserve links.
            Assert.True((await client.UpdateEntryAsync(new(id, new(new(2026, 11, 1), EntryKind.Income, 15m, "Salary")), Context.Empty)).IsSuccess);
            Assert.Single((await client.ListByCalendarEntryAsync(new(taskId), Context.Empty)).Value!);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UpdateEntryAsync(new(id, new(new(2026, 11, 1), EntryKind.Income, 15m, "Salary", CalendarEntryIds: [])), Context.Empty, cancelled.Token));
            Assert.Single((await client.ListByCalendarEntryAsync(new(taskId), Context.Empty)).Value!);
        });
        OfficialRpcHarness.Value(await host.Call("ledger.update", new
        {
            id,
            entry = new
            {
                date = "2026-11-01",
                kind = "Income",
                amount = 15m,
                category = "Salary",
                calendarEntryIds = Array.Empty<Guid>()
            }
        }));
        Assert.Empty(OfficialRpcHarness.Value(await host.Call("ledger.by-calendar-entry", new
        {
            calendarEntryId = eventId
        })).EnumerateArray());
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            await client.UpdateEntryAsync(new(id, new(new(2026, 11, 1), EntryKind.Income, 15m, "Salary", CalendarEntryIds: [eventId])), Context.Empty);
            await client.DeleteEntryAsync(new(id), Context.Empty);
            Assert.Empty((await client.ListByCalendarEntryAsync(new(eventId), Context.Empty)).Value!);
        });
    }

    [Fact]
    public async Task UpgradePreservesExistingLedgerRowsWithEmptyCalendarLinks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await LedgerPersistenceTests.Migrate(connection, 3);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO OfficialLedgerEntries (Id, Date, Kind, AmountInFen, Category, Note, TenantId) VALUES ($id, '2026-09-01', 0, 1234, 'Food', 'Existing', 'tenant')";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        await command.ExecuteNonQueryAsync();
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "004_calendar_links.sql"));
        await command.ExecuteNonQueryAsync();
        command.CommandText = "SELECT AmountInFen, Note, CalendarEntryIdsJson FROM OfficialLedgerEntries";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1234, reader.GetInt64(0));
        Assert.Equal("Existing", reader.GetString(1));
        Assert.Equal("[]", reader.GetString(2));
    }

    [Fact]
    public async Task OptionalDiscoveryRequiresAllCapabilitiesAndToleratesMissingProvider()
    {
        var helper = typeof(LedgerPage).Assembly.GetType("Quantum.OfficialPlugins.OptionalRpc")!;
        var discover = helper.GetMethod("DiscoverAsync")!;
        var rpc = new IntegrationInvoker("partial.calendar", "calendar.list");
        var task = (Task<Dictionary<string, string>>)discover.Invoke(null, [rpc, new[] { "calendar.list", "calendar.get" }])!;
        Assert.Empty(await task);
        rpc.Available = false;
        task = (Task<Dictionary<string, string>>)discover.Invoke(null, [rpc, new[] { "calendar.list" }])!;
        Assert.Empty(await task);
        task = (Task<Dictionary<string, string>>)discover.Invoke(null, [null, new[] { "calendar.list" }])!;
        Assert.Empty(await task);
    }

    [Fact]
    public async Task CalendarReadsStringLedgerKindsThroughActualHostInvoker()
    {
        await using var connection = new SqliteConnection($"Data Source=host-enums-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await LedgerPersistenceTests.Migrate(connection);
        var builder = LedgerPersistenceTests.Configure(connection.ConnectionString);
        await using var host = new OfficialRpcHarness(builder.Services, "replacement.ledger", typeof(ILedgerRpcService).Assembly);
        var eventId = Guid.NewGuid();
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            await client.CreateEntryAsync(new(new(2026, 10, 20), EntryKind.Expense, 12m, "Food", CalendarEntryIds: [eventId]), Context.Empty);
            await client.CreateEntryAsync(new(new(2026, 10, 20), EntryKind.Income, 30m, "Refund", CalendarEntryIds: [eventId]), Context.Empty);
        });
        var invoker = host.CreateInvoker();
        var wire = await invoker.InvokeAsync<JsonElement>("replacement.ledger.ledger.by-calendar-entry", new
        {
            calendarEntryId = eventId
        }, Context.Empty);
        Assert.True(wire.IsSuccess);
        Assert.Equal(new[] { "Expense", "Income" }, wire.Value.EnumerateArray().Select(row => row.GetProperty("kind").GetString()).Order().ToArray());
        var incompatible = await invoker.InvokeAsync<NumericLedgerProjection[]>("replacement.ledger.ledger.by-calendar-entry", new
        {
            calendarEntryId = eventId
        }, Context.Empty);
        Assert.False(incompatible.IsSuccess);
        Assert.Equal("rpc_invalid_result", incompatible.ErrorCode);
        Assert.Equal("The RPC result does not match the requested response type.", incompatible.Message);
        var page = new CalendarPage(null!, invoker);
        SetLocalizer<OfficialPlugins.Calendar.Localization.PluginStrings>(page);
        var item = new OfficialPlugins.Calendar.Application.CalendarEntryDetails(eventId, "Trip", "", OfficialPlugins.Calendar.Domain.CalendarEntryKind.Event, new(2026, 10, 20), new(9, 0), null, false, "blue");
        await Invoke(page, "ViewLedgerAsync", item);
        Assert.Null(Field(page, "_errorMessage"));
        Assert.NotNull(Field(page, "_ledgerViewing"));
        var entries = JsonSerializer.SerializeToElement(Field(page, "_linkedLedgerEntries"));
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal(new[] { 12m, 30m }, entries.EnumerateArray().Select(entry => entry.GetProperty("Amount").GetDecimal()).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalendarUsesReplacementLedgerQualifiedCapabilityAndHandlesProviderRemoval(bool legacyNumbers)
    {
        var rpc = new IntegrationInvoker("alternative.finance", "ledger.by-calendar-entry") { LegacyNumbers = legacyNumbers };
        var page = new CalendarPage(null!, rpc);
        SetLocalizer<OfficialPlugins.Calendar.Localization.PluginStrings>(page);
        var item = new OfficialPlugins.Calendar.Application.CalendarEntryDetails(Guid.NewGuid(), "Trip", "", OfficialPlugins.Calendar.Domain.CalendarEntryKind.Event, new(2026, 10, 20), new(9, 0), null, false, "blue");
        await Invoke(page, "ViewLedgerAsync", item);
        Assert.Equal("alternative.finance.ledger.by-calendar-entry", rpc.LastCall);
        Assert.NotNull(Field(page, "_ledgerViewing"));
        var linked = JsonSerializer.SerializeToElement(Field(page, "_linkedLedgerEntries"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(12m, Assert.Single(linked.EnumerateArray()).GetProperty("amount").GetDecimal());
        rpc.Available = false;
        await Invoke(page, "ViewLedgerAsync", item);
        Assert.Empty((Dictionary<string, string>)Field(page, "_ledgerMethods")!);
        Assert.NotNull(Field(page, "_errorMessage"));
    }

    [Fact]
    public async Task LedgerReadsCalendarStringKindsAndDatesOverJsonAndShowsDeletedReferences()
    {
        var rpc = new IntegrationInvoker("alternative.calendar", "calendar.list", "calendar.get");
        var page = new LedgerPage(null!, rpc);
        SetLocalizer<OfficialPlugins.Ledger.Localization.PluginStrings>(page);
        var helper = typeof(LedgerPage).Assembly.GetType("Quantum.OfficialPlugins.OptionalRpc")!;
        var discovery = (Task<Dictionary<string, string>>)helper.GetMethod("DiscoverAsync")!.Invoke(null, [rpc, new[] { "calendar.list", "calendar.get" }])!;
        SetField(page, "_calendarMethods", await discovery);
        SetField(page, "_candidateMonth", new DateOnly(2026, 10, 1));
        await Invoke(page, "LoadCalendarCandidates");
        Assert.Equal("2026-10-01", rpc.LastPayload.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-31", rpc.LastPayload.GetProperty("endDate").GetString());
        Assert.Single((Array)Field(page, "_calendarCandidates")!);
        var entry = new LedgerEntryDetails(Guid.NewGuid(), new(2026, 10, 1), EntryKind.Expense, 5, "Food", "", [], [Guid.NewGuid()]);
        await Invoke(page, "ViewCalendarLinks", entry);
        Assert.NotNull(Field(page, "_calendarViewing"));
        Assert.Single((System.Collections.IEnumerable)Field(page, "_calendarLinks")!);
    }

    private sealed record NumericLedgerProjection(int Kind);

    private static object? Field(object page, string name) => page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void SetField(object page, string name, object value) => page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Invoke(object page, string name, params object[] args) => (Task)page.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args)!;
    private static void SetLocalizer<T>(object page)
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        page.GetType().GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, new StringLocalizer<T>(factory));
    }

    private sealed class IntegrationInvoker(string provider, params string[] methods) : IRpcInvoker
    {
        public bool Available { get; set; } = true;
        public bool LegacyNumbers
        {
            get; init;
        }
        public string? LastCall
        {
            get; private set;
        }
        public JsonElement LastPayload
        {
            get; private set;
        }
        public Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
        {
            if (!Available)
            {
                return Task.FromResult<Result<T>>(Result.Fail("unavailable", "Provider stopped"));
            }
            object response;
            if (rpcName == "quantum.rpc.catalog")
            {
                response = new
                {
                    schemaVersion = 1,
                    catalogRpcName = rpcName,
                    services = new[] { new { pluginId = provider, serviceName = methods[0].Split('.')[0], methods = methods.Select(name => new { canonicalName = name, qualifiedName = provider + "." + name }) } }
                };
            }
            else
            {
                LastCall = rpcName;
                LastPayload = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                response = rpcName.EndsWith("calendar.list", StringComparison.Ordinal)
                    ? new[] { new { id = Guid.NewGuid(), title = "Task", date = "2026-10-20", kind = "Task", notes = "", startTime = "09:00:00", endTime = (string?)null, isCompleted = false } }
                    : rpcName.EndsWith("calendar.get", StringComparison.Ordinal) ? new
                    {
                        entry = (object?)null
                    }
                    : new[] { new { id = Guid.NewGuid(), date = "2026-09-01", kind = JsonSerializer.SerializeToElement(LegacyNumbers ? (object)0 : "Expense"), amount = 12m, category = "Food", note = "Lunch" } };
            }
            Result<T> result = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(response), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return Task.FromResult(result);
        }
        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
