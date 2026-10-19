using System.Reflection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NOF.Contract;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Localization;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Xunit;
using LedgerPage = Quantum.OfficialPlugins.Ledger.Pages.Ledger;

namespace Quantum.LedgerPlugin.Tests;

public sealed class LedgerDeleteConfirmationTests
{
    [Fact]
    public async Task DeleteRequiresSecondClickAndChangingTargetOrEscapeCancelsConfirmation()
    {
        await using var connection = new SqliteConnection($"Data Source=ledger-confirm-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await LedgerPersistenceTests.Migrate(connection);

        var builder = LedgerPersistenceTests.Configure(connection.ConnectionString);
        await using var host = new OfficialRpcHarness(builder.Services, "quantum.plugin.ledger", typeof(ILedgerRpcService).Assembly);
        await host.UseClient<ILedgerRpcServiceClient>(async client =>
        {
            var first = (await client.CreateEntryAsync(new(new(2026, 10, 1), EntryKind.Expense, 12m, "Food"), Context.Empty)).Value!;
            var second = (await client.CreateEntryAsync(new(new(2026, 10, 1), EntryKind.Expense, 24m, "Food"), Context.Empty)).Value!;
            var page = new LedgerPage(client);
            var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            typeof(LedgerPage).GetProperty("L", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, new StringLocalizer<PluginStrings>(factory));

            await ClickDelete(page, first);
            Assert.NotNull((await client.GetEntryAsync(new(first.Id), Context.Empty)).Value!.Entry);
            await ClickDelete(page, second);
            await ClickDelete(page, first);
            Assert.NotNull((await client.GetEntryAsync(new(first.Id), Context.Empty)).Value!.Entry);
            Assert.NotNull((await client.GetEntryAsync(new(second.Id), Context.Empty)).Value!.Entry);

            typeof(LedgerPage).GetMethod("HandleKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [new KeyboardEventArgs { Key = "Escape" }]);
            await ClickDelete(page, first);
            Assert.NotNull((await client.GetEntryAsync(new(first.Id), Context.Empty)).Value!.Entry);
            await ClickDelete(page, first);
            Assert.Null((await client.GetEntryAsync(new(first.Id), Context.Empty)).Value!.Entry);
            Assert.NotNull((await client.GetEntryAsync(new(second.Id), Context.Empty)).Value!.Entry);
        });
    }

    private static Task ClickDelete(LedgerPage page, LedgerEntryDetails entry)
        => (Task)typeof(LedgerPage).GetMethod("RequestDelete", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [entry])!;
}
