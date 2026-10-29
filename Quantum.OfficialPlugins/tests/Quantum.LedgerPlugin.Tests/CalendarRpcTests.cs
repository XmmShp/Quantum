using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Calendar;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
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
            await using var migration = connection.CreateCommand();
            migration.CommandText = await File.ReadAllTextAsync(file);
            await migration.ExecuteNonQueryAsync();
        }
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Clear();
        builder.Services.AddNOFApplication();
        builder.AddNOFEntityFrameworkCore();
        builder.UseDbContext<LedgerTestDb>().WithTenantMode(TenantMode.SharedDatabase).WithConnectionString(connection.ConnectionString)
            .WithOptions((options, cs) => options.UseSqlite(cs).EnableServiceProviderCaching(false));
        CalendarPluginInitializer.Initialize(builder.Services);
        builder.Services.AddLogging();
        builder.Services.AddSingleton(TimeProvider.System);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.calendar", typeof(ICalendarRpcService).Assembly);
        await host.AssertCatalog("quantum.plugin.calendar", "calendar", ["list", "list-tasks", "get", "create", "update", "set-completed", "delete", "mobile-refresh"]);
        var request = new SaveCalendarEntryRequest
        {
            Title = "RPC task",
            Notes = "note",
            Kind = CalendarEntryKind.Task,
            Date = new(2026, 10, 19),
            StartTime = new(12, 0),
            EndTime = null,
            Style = "green"
        };
        var created = OfficialRpcHarness.Value(await host.Call("quantum.plugin.calendar.calendar.create", request));
        var id = created.GetProperty("id").GetGuid();
        var range = new ListCalendarEntriesRequest
        {
            StartDate = new(2026, 10, 1),
            EndDate = new(2026, 10, 31)
        };
        Assert.Equal(1, OfficialRpcHarness.Value(await host.Call("calendar.list", range)).GetArrayLength());
        Assert.Equal(1, OfficialRpcHarness.Value(await host.Call("calendar.list-tasks", new ListCalendarTasksRequest
        {
            IncludeCompleted = false
        })).GetArrayLength());
        OfficialRpcHarness.Value(await host.Call("calendar.update", new UpdateCalendarEntryRequest
        {
            Id = id,
            Entry = request with
            {
                Title = "Updated"
            }
        }));
        var read = OfficialRpcHarness.Value(await host.Call("calendar.get", new CalendarEntryRequest
        {
            Id = id
        }));
        Assert.Equal("Updated", read.GetProperty("entry").GetProperty("title").GetString());
        OfficialRpcHarness.Value(await host.Call("calendar.set-completed", new SetCalendarEntryCompletedRequest
        {
            Id = id,
            IsCompleted = true
        }));
        Assert.Equal(0, OfficialRpcHarness.Value(await host.Call("calendar.list-tasks", new ListCalendarTasksRequest
        {
            IncludeCompleted = false
        })).GetArrayLength());
        Assert.True((await host.Call("calendar.delete", new CalendarEntryRequest
        {
            Id = id
        }, false)).GetProperty("isSuccess").GetBoolean());
        var missing = OfficialRpcHarness.Value(await host.Call("calendar.get", new CalendarEntryRequest
        {
            Id = id
        }));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, missing.GetProperty("entry").ValueKind);
        var invalid = await host.Call("calendar.create", request with
        {
            Title = " "
        });
        Assert.Equal("calendar_invalid_request", invalid.GetProperty("errorCode").GetString());
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
            var englishFailure = await host.Call("calendar.create", request with
            {
                Title = new string('x', 121)
            });
            Assert.Equal("calendar_invalid_request", englishFailure.GetProperty("errorCode").GetString());
            Assert.Equal("Titles cannot exceed 120 characters.", englishFailure.GetProperty("message").GetString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
        }
        var notFound = await host.Call("calendar.update", new UpdateCalendarEntryRequest
        {
            Id = id,
            Entry = request
        });
        Assert.Equal("calendar_entry_not_found", notFound.GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("calendar.list", range, cancellationToken: cancelled.Token));
        await host.UseClient<ICalendarRpcServiceClient>(async client =>
        {
            var context = NOF.Contract.Context.Empty;
            var local = await client.CreateEntryAsync(request, context);
            Assert.True(local.IsSuccess, local.Message);
            var localId = local.Value!.Id;
            var invalidUpdate = await client.UpdateEntryAsync(new()
            {
                Id = localId,
                Entry = request with
                {
                    Title = " "
                }
            }, context);
            Assert.False(invalidUpdate.IsSuccess);
            Assert.Equal(request.Title, (await client.GetEntryAsync(new()
            {
                Id = localId
            }, context)).Value!.Entry!.Title);
            Assert.True((await client.SetCompletedAsync(new()
            {
                Id = localId,
                IsCompleted = true
            }, context)).IsSuccess);
            Assert.Empty((await client.ListTasksAsync(new()
            {
                IncludeCompleted = false
            }, context)).Value!);
            Assert.True((await client.GetEntryAsync(new()
            {
                Id = localId
            }, context)).Value!.Entry!.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DeleteEntryAsync(new()
            {
                Id = localId
            }, context, cancelled.Token));
            Assert.True((await client.DeleteEntryAsync(new()
            {
                Id = localId
            }, context)).IsSuccess);
            Assert.Null((await client.GetEntryAsync(new()
            {
                Id = localId
            }, context)).Value!.Entry);
        });
    }
}
