using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.Plugins;
using System.Text.Json;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void PropertyContractsRetainOptionalDefaultsAndEnforceRequiredFieldsOverHostJson()
    {
        using var serializer = new PluginRpcSerializer();
        var ledger = serializer.Deserialize<SaveLedgerEntryRequest>(JsonSerializer.SerializeToElement(new
        {
            category = "Food",
            amount = 12m,
            kind = "Expense",
            date = "2026-10-20"
        }))!;
        Assert.Equal(string.Empty, ledger.Note);
        Assert.Null(ledger.RelatedEntryIds);
        Assert.Null(ledger.CalendarEntryIds);

        var empty = JsonSerializer.SerializeToElement(new
        {
        });
        Assert.Throws<JsonException>(() => serializer.Deserialize<SaveLedgerEntryRequest>(empty));
        Assert.Throws<JsonException>(() => serializer.Deserialize<LedgerEntryRequest>(empty));
        Assert.Throws<JsonException>(() => serializer.Deserialize<ListCalendarEntriesRequest>(empty));
        Assert.True(serializer.Deserialize<ListCalendarTasksRequest>(empty)!.IncludeCompleted);

        var connect = serializer.Deserialize<CodexConnectRequest>(empty)!;
        Assert.Equal(string.Empty, connect.WorkingDirectory);
        Assert.Equal("gpt-5.6-luna", connect.Model);
        Assert.Null(serializer.Deserialize<CodexNewThreadRequest>(empty)!.Model);

        ledger.Amount = 24m;
        var copied = serializer.Deserialize<SaveLedgerEntryRequest>(serializer.Serialize(ledger, ledger.GetType()))!;
        Assert.Equal(24m, copied.Amount);
        Assert.Equal(EntryKind.Expense, copied.Kind);
        Assert.Equal(new DateOnly(2026, 10, 20), copied.Date);
        Assert.Equal("Food", copied.Category);
    }
}
