using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Hosting;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class CalendarRpcTests
{
    [Fact]
    public async Task ExistingCalendarRpcCatalogAndAllSevenOperationsWorkWithShippedMigrations()
    {
        await using var connection = new SqliteConnection($"Data Source=calendar-rpc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "calendar-migrations"), "*.sql").Order(StringComparer.Ordinal))
        {
            await using var migration = connection.CreateCommand(); migration.CommandText = await File.ReadAllTextAsync(file);
            await migration.ExecuteNonQueryAsync();
        }
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Clear(); builder.Services.AddNOFApplication(); builder.AddNOFEntityFrameworkCore();
        builder.UseDbContext<LedgerTestDb>().WithTenantMode(TenantMode.SharedDatabase).WithConnectionString(connection.ConnectionString)
            .WithOptions((options, cs) => options.UseSqlite(cs).EnableServiceProviderCaching(false));
        CalendarPluginInitializer.Initialize(builder.Services);
        builder.Services.AddLogging(); builder.Services.AddSingleton(TimeProvider.System);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.calendar", typeof(ICalendarRpcService).Assembly);
        await host.AssertCatalog("quantum.plugin.calendar", "calendar", ["list", "list-tasks", "get", "create", "update", "set-completed", "delete"]);
        var request = new SaveCalendarEntryRequest("RPC task", "note", CalendarEntryKind.Task, new(2026, 10, 19), new(12, 0), null, "green");
        var created = OfficialRpcHarness.Value(await host.Call("quantum.plugin.calendar.calendar.create", request));
        var id = created.GetProperty("id").GetGuid();
        var range = new ListCalendarEntriesRequest(new(2026, 10, 1), new(2026, 10, 31));
        Assert.Equal(1, OfficialRpcHarness.Value(await host.Call("calendar.list", range)).GetArrayLength());
        Assert.Equal(1, OfficialRpcHarness.Value(await host.Call("calendar.list-tasks", new ListCalendarTasksRequest(false))).GetArrayLength());
        OfficialRpcHarness.Value(await host.Call("calendar.update", new UpdateCalendarEntryRequest(id, request with { Title = "Updated" })));
        var read = OfficialRpcHarness.Value(await host.Call("calendar.get", new CalendarEntryRequest(id)));
        Assert.Equal("Updated", read.GetProperty("entry").GetProperty("title").GetString());
        OfficialRpcHarness.Value(await host.Call("calendar.set-completed", new SetCalendarEntryCompletedRequest(id, true)));
        Assert.Equal(0, OfficialRpcHarness.Value(await host.Call("calendar.list-tasks", new ListCalendarTasksRequest(false))).GetArrayLength());
        Assert.True((await host.Call("calendar.delete", new CalendarEntryRequest(id), false)).GetProperty("isSuccess").GetBoolean());
        var missing = OfficialRpcHarness.Value(await host.Call("calendar.get", new CalendarEntryRequest(id)));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, missing.GetProperty("entry").ValueKind);
        var invalid = await host.Call("calendar.create", request with { Title = " " });
        Assert.Equal("calendar_invalid_request", invalid.GetProperty("errorCode").GetString());
        var notFound = await host.Call("calendar.update", new UpdateCalendarEntryRequest(id, request));
        Assert.Equal("calendar_entry_not_found", notFound.GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("calendar.list", range, cancellationToken: cancelled.Token));
    }
}
