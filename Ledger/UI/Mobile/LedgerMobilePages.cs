using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.OfficialPlugins.Ledger.UI.Localization;
using Quantum.Plugin.Abstraction;
using System.Globalization;
using System.Resources;
using Ui = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.OfficialPlugins.Ledger.UI.Mobile;

public sealed class LedgerMobilePages : ILedgerMobilePages
{
    private static readonly ResourceManager Resources = new(typeof(PluginStrings));
    private readonly ILedgerRpcServiceClient _client;
    private readonly IQuantumMobilePages? _pages;
    private readonly TimeProvider _clock;

    public LedgerMobilePages(ILedgerRpcServiceClient client, IQuantumMobilePages? pages = null, TimeProvider? timeProvider = null)
    {
        _client = client;
        _pages = pages;
        _clock = timeProvider ?? TimeProvider.System;
    }

    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pages is null)
        {
            throw new DomainException("mobile_unavailable", "This host does not support mobile pages.");
        }
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var summary = await _client.GetSummaryAsync(new()
        {
            Month = today
        }, Context.Empty, cancellationToken);
        var entries = await _client.ListEntriesAsync(new()
        {
            Month = today
        }, Context.Empty, cancellationToken);
        var nodes = new List<MobileNode>
        {
            Ui.Text("title", L("Ledger")).Appearance("title"),
            Ui.Text("month", today.ToString("yyyy-MM", CultureInfo.InvariantCulture) + " · CNY"),
            Ui.Text("refresh-help", L("After changes, prepare latest data, then use the phone's Refresh button.")).Appearance("caption"),
            Ui.Button("refresh", L("Prepare latest data")).OnClick(Ui.Rpc("quantum.plugin.ledger.ledger.mobile-refresh"))
        };
        if (summary.IsSuccess && entries.IsSuccess)
        {
            var totals = summary.Value!;
            nodes.Add(Ui.Text("income", L("Income") + ": " + totals.Income.ToString("F2", CultureInfo.InvariantCulture)));
            nodes.Add(Ui.Text("expense", L("Expense") + ": " + totals.Expense.ToString("F2", CultureInfo.InvariantCulture)));
            nodes.Add(Ui.Text("balance", L("Balance") + ": " + totals.Balance.ToString("F2", CultureInfo.InvariantCulture)));
            nodes.Add(Ui.Text("recent-title", L("Recent entries")).Appearance("title"));
            if (entries.Value!.Length == 0)
            {
                nodes.Add(Ui.Text("empty", L("No items to display.")));
            }
            foreach (var entry in entries.Value!.Take(15))
            {
                nodes.Add(Ui.Text("entry-" + entry.Id.ToString("N"),
                    $"{entry.Date:MM-dd} · {L(entry.Kind == EntryKind.Income ? "Income" : "Expense")} · {entry.Amount.ToString("F2", CultureInfo.InvariantCulture)} · {entry.Category}"));
            }
        }
        else
        {
            nodes.Add(Ui.Text("load-error", L("Unable to load data. Prepare latest data to retry.")).Appearance("error"));
        }
        nodes.AddRange([
            Ui.Divider("form-divider"), Ui.Text("form-title", L("Quick entry")).Appearance("title"),
            Ui.TextField("amount").Hint(L("Amount, for example 12.50")).Limit(16),
            Ui.TextField("category").Hint(L("Category")).Limit(40),
            Ui.TextField("note").Hint(L("Notes")).Limit(1000),
            Ui.Toggle("income-toggle", L("Record as income")),
            Ui.Text("date-help", L("Saved for today's desktop local date. Unchecked means expense.")).Appearance("caption"),
            Ui.Button("create", L("Save entry")).OnClick(Ui.Rpc("quantum.plugin.ledger.ledger.mobile-create")
                .Input("amountText", "amount").Input("category", "category").Input("note", "note").Input("isIncome", "income-toggle"))
        ]);
        _pages.Publish(Ui.Page("ledger", L("Ledger"), Ui.Scroll("scroll", Ui.VStack("root", nodes.ToArray()))));
    }

    private static string L(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
