using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;
using Quantum.OfficialPlugins.Calendar;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Ledger.Rpc;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class MobilePagesTests
{
    [Fact]
    public async Task LedgerLifecyclePublishesLocalizedPageAndButtonsWriteThroughRealHostRpc()
    {
        await using var fixture = await Fixture.Create(calendar: false);
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            await fixture.Host.UseServices(OfficialPlugins.Ledger.LedgerPlugin.StartAsync);
            Assert.Equal("记账", fixture.Pages.Pages["ledger"].Title);
            var saved = OfficialRpcHarness.Value(await fixture.Pages.Click(fixture.Host, "ledger", "create", new()
            {
                ["amount"] = JsonSerializer.SerializeToElement("12.50"),
                ["category"] = JsonSerializer.SerializeToElement("午餐"),
                ["note"] = JsonSerializer.SerializeToElement("手机记录"),
                ["income-toggle"] = JsonSerializer.SerializeToElement(false)
            }));
            Assert.Equal(12.50m, saved.GetProperty("amount").GetDecimal());
            Assert.Equal("Expense", saved.GetProperty("kind").GetString());
            Assert.Equal("2026-10-23", saved.GetProperty("date").GetString());
            Assert.Equal("手机记录", saved.GetProperty("note").GetString());
            Assert.True((await fixture.Pages.Click(fixture.Host, "ledger", "refresh")).GetProperty("isSuccess").GetBoolean());
            Assert.Contains("12.50", fixture.Pages.Node("ledger", "expense").Text);
            Assert.Contains("支出", fixture.Pages.Node("ledger", "expense").Text);
            await fixture.Host.UseServices(OfficialPlugins.Ledger.LedgerPlugin.StopAsync);
            Assert.Empty(fixture.Pages.Pages);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("1,23")]
    [InlineData("NaN")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("0.001")]
    [InlineData("1000000000")]
    public async Task MobileAmountParsingPreservesBusinessValidationAndRecoversAfterFailure(string amount)
    {
        await using var fixture = await Fixture.Create(calendar: false);
        var failed = await fixture.Host.Call("quantum.plugin.ledger.ledger.mobile-create", new CreateMobileLedgerEntryRequest
        {
            AmountText = amount,
            Category = "Food"
        });
        Assert.False(failed.GetProperty("isSuccess").GetBoolean());
        Assert.DoesNotContain(failed.GetProperty("message").GetString()!, c => c is >= '\u4e00' and <= '\u9fff');
        var created = OfficialRpcHarness.Value(await fixture.Host.Call("ledger.mobile-create", new CreateMobileLedgerEntryRequest
        {
            AmountText = "0.10",
            Category = "Income",
            IsIncome = true
        }));
        Assert.Equal("Income", created.GetProperty("kind").GetString());
        var summary = OfficialRpcHarness.Value(await fixture.Host.Call("ledger.summary", new LedgerMonthRequest { Month = new(2026, 10, 23) }));
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(0.10m, summary.GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task CalendarButtonsCreateTodayTaskAndCompleteItAcrossHostJsonBoundary()
    {
        await using var fixture = await Fixture.Create(calendar: true);
        await fixture.Host.UseServices(OfficialPlugins.Calendar.CalendarPlugin.StartAsync);
        var created = OfficialRpcHarness.Value(await fixture.Pages.Click(fixture.Host, "calendar", "create", new()
        {
            ["task-title"] = JsonSerializer.SerializeToElement("移动待办"),
            ["task-notes"] = JsonSerializer.SerializeToElement("备注")
        }));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Task", created.GetProperty("kind").GetString());
        Assert.Equal("2026-10-23", created.GetProperty("date").GetString());
        Assert.True((await fixture.Pages.Click(fixture.Host, "calendar", "refresh")).GetProperty("isSuccess").GetBoolean());
        var complete = await fixture.Pages.Click(fixture.Host, "calendar", "complete-" + id.ToString("N"));
        Assert.True(complete.GetProperty("isSuccess").GetBoolean());
        var entry = OfficialRpcHarness.Value(await fixture.Host.Call("calendar.get", new CalendarEntryRequest { Id = id })).GetProperty("entry");
        Assert.True(entry.GetProperty("isCompleted").GetBoolean());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Host.Call("calendar.mobile-refresh", new RefreshCalendarMobilePageRequest(), expectsValue: false, cancellationToken: cancelled.Token));
        await fixture.Host.UseServices(OfficialPlugins.Calendar.CalendarPlugin.StopAsync);
        Assert.Empty(fixture.Pages.Pages);
    }

    [Fact]
    public async Task HostsWithoutMobileCapabilityKeepNormalPluginStartup()
    {
        await using var fixture = await Fixture.Create(calendar: false, mobile: false);
        await fixture.Host.UseServices(OfficialPlugins.Ledger.LedgerPlugin.StartAsync);
        var failure = await fixture.Host.Call("ledger.mobile-refresh", new RefreshLedgerMobilePageRequest(), expectsValue: false);
        Assert.Equal("mobile_unavailable", failure.GetProperty("errorCode").GetString());
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Fixture(SqliteConnection connection, OfficialRpcHarness host, CapturedMobilePages pages) : IAsyncDisposable
    {
        public OfficialRpcHarness Host { get; } = host;
        public CapturedMobilePages Pages { get; } = pages;
        public static async Task<Fixture> Create(bool calendar, bool mobile = true)
        {
            var connection = new SqliteConnection($"Data Source=mobile-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            if (calendar)
            {
                foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "calendar-migrations"), "*.sql").Order(StringComparer.Ordinal))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = await File.ReadAllTextAsync(file);
                    await command.ExecuteNonQueryAsync();
                }
            }
            else
            {
                await LedgerPersistenceTests.Migrate(connection);
            }
            var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.Clear();
            builder.Services.AddNOFApplication();
            builder.AddNOFEntityFrameworkCore();
            builder.UseDbContext<LedgerTestDb>().WithTenantMode(TenantMode.SharedDatabase).WithConnectionString(connection.ConnectionString)
                .WithOptions((options, cs) => options.UseSqlite(cs).EnableServiceProviderCaching(false));
            Assembly assembly;
            if (calendar)
            {
                CalendarPluginInitializer.Initialize(builder.Services);
                assembly = typeof(ICalendarRpcService).Assembly;
            }
            else
            {
                OfficialPlugins.Ledger.LedgerPlugin.ConfigureServices(builder.Services);
                assembly = typeof(ILedgerRpcService).Assembly;
            }
            builder.Services.AddLogging();
            builder.Services.AddSingleton<TimeProvider>(new TestClock());
            var pages = new CapturedMobilePages();
            if (mobile)
            {
                builder.Services.AddSingleton<IQuantumMobilePages>(pages);
            }
            return new(connection, new(builder.Services, "quantum.plugin." + (calendar ? "calendar" : "ledger"), assembly), pages);
        }
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
