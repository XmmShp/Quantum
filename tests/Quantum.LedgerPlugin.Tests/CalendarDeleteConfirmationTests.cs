using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Calendar;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using System.Reflection;
using Xunit;
using CalendarPage = Quantum.OfficialPlugins.Calendar.UI.Pages.Calendar;

namespace Quantum.LedgerPlugin.Tests;

public sealed class CalendarDeleteConfirmationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteRequiresSecondClickOnSameControlAndEscapeCancels(bool isTaskList)
    {
        await using var connection = new SqliteConnection($"Data Source=calendar-confirm-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
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

        await host.UseClient<ICalendarRpcServiceClient>(async client =>
        {
            var created = await client.CreateEntryAsync(new()
            {
                Title = "Confirmation fixture",
                Notes = "",
                Kind = CalendarEntryKind.Task,
                Date = DateOnly.FromDateTime(DateTime.Today),
                StartTime = new(12, 0),
                EndTime = null,
                Style = "green"
            }, Context.Empty);
            Assert.True(created.IsSuccess);
            var id = created.Value!.Id;
            var page = new CalendarPage(client);

            await ClickDelete(page, id, isTaskList);
            Assert.NotNull((await client.GetEntryAsync(new()
            {
                Id = id
            }, Context.Empty)).Value!.Entry);

            typeof(CalendarPage).GetMethod("HandleKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [new KeyboardEventArgs { Key = "Escape" }]);
            await ClickDelete(page, id, isTaskList);
            Assert.NotNull((await client.GetEntryAsync(new()
            {
                Id = id
            }, Context.Empty)).Value!.Entry);

            // The same task in the other list must require its own confirmation.
            await ClickDelete(page, id, !isTaskList);
            Assert.NotNull((await client.GetEntryAsync(new()
            {
                Id = id
            }, Context.Empty)).Value!.Entry);
            await ClickDelete(page, id, isTaskList);
            Assert.NotNull((await client.GetEntryAsync(new()
            {
                Id = id
            }, Context.Empty)).Value!.Entry);

            await ClickDelete(page, id, isTaskList);
            Assert.Null((await client.GetEntryAsync(new()
            {
                Id = id
            }, Context.Empty)).Value!.Entry);
        });
    }

    private static Task ClickDelete(CalendarPage page, Guid id, bool isTaskList)
        => (Task)typeof(CalendarPage).GetMethod("DeleteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [id, isTaskList])!;
}
