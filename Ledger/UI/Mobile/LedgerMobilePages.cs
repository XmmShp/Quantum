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
            Ui.Text("month", today.ToString("yyyy-MM", CultureInfo.InvariantCulture) + " · CNY").Appearance("caption")
        };
        if (summary.IsSuccess && entries.IsSuccess)
        {
            var totals = summary.Value!;
            nodes.Add(Ui.HStack("totals",
                Ui.VStack("income-card", Ui.Text("income-label", L("Income")).Appearance("caption"), Ui.Text("income", totals.Income.ToString("F2", CultureInfo.InvariantCulture)).Appearance("title")),
                Ui.VStack("expense-card", Ui.Text("expense-label", L("Expense")).Appearance("caption"), Ui.Text("expense", totals.Expense.ToString("F2", CultureInfo.InvariantCulture)).Appearance("title")),
                Ui.VStack("balance-card", Ui.Text("balance-label", L("Balance")).Appearance("caption"), Ui.Text("balance", totals.Balance.ToString("F2", CultureInfo.InvariantCulture)).Appearance("title"))));
            var recent = new List<MobileNode> { Ui.Text("recent-title", L("Recent entries")).Appearance("title") };
            if (entries.Value!.Length == 0)
            {
                recent.Add(Ui.Text("empty", L("No items to display.")).Appearance("caption"));
            }
            foreach (var entry in entries.Value!.Take(15))
            {
                var id = entry.Id.ToString("N");
                recent.Add(Ui.HStack("entry-row-" + id,
                    Ui.VStack("entry-detail-" + id, Ui.Text("entry-" + id, entry.Category), Ui.Text("entry-date-" + id, entry.Date.ToString("MM-dd", CultureInfo.InvariantCulture)).Appearance("caption")),
                    Ui.Text("entry-amount-" + id, (entry.Kind == EntryKind.Income ? "+" : "−") + entry.Amount.ToString("F2", CultureInfo.InvariantCulture)).Appearance("title")));
                recent.Add(Ui.Divider("entry-line-" + id));
            }
            if (recent.Last().Type == "Divider")
            {
                recent.RemoveAt(recent.Count - 1);
            }
            nodes.Add(Ui.VStack("recent", recent.ToArray()));
        }
        else
        {
            nodes.Add(Ui.Text("load-error", L("Unable to load data. Reconnecting automatically.")).Appearance("error"));
        }
        nodes.Insert(Math.Min(2, nodes.Count), Ui.VStack("entry-form",
            Ui.Text("form-title", L("Quick entry")).Appearance("title"),
            Ui.HStack("entry-fields", Ui.TextField("amount").Hint(L("Amount, for example 12.50")).Limit(16), Ui.TextField("category").Hint(L("Category")).Limit(40)),
            Ui.TextField("note").Hint(L("Notes")).Limit(1000),
            Ui.HStack("entry-submit", Ui.Toggle("income-toggle", L("Income")),
                Ui.Button("create", L("Save entry")).OnClick(Ui.Rpc("quantum.plugin.ledger.ledger.mobile-create")
                    .Input("amountText", "amount").Input("category", "category").Input("note", "note").Input("isIncome", "income-toggle"))),
            Ui.Text("date-help", L("Saved for today's desktop local date. Unchecked means expense.")).Appearance("caption")));
        _pages.Publish(Ui.Page("ledger", L("Ledger"), Ui.Scroll("scroll", Ui.VStack("root", nodes.ToArray()))));
    }

    private static string L(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
