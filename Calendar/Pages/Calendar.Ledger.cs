using Microsoft.AspNetCore.Components;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Calendar.Pages;

public partial class Calendar
{
    private readonly IRpcInvoker? _invoker;
    private Dictionary<string, string> _ledgerMethods = [];
    private CalendarEntryDetails? _ledgerViewing;
    private LinkedLedgerEntry[] _linkedLedgerEntries = [];
    private ElementReference _ledgerDialog;

    private Task ViewLedgerAsync(CalendarEntryDetails item) => ExecuteAsync(async () =>
    {
        var methods = await OptionalRpc.DiscoverAsync(_invoker, "ledger.by-calendar-entry");
        if (methods.Count == 0)
        {
            _ledgerMethods = [];
            _errorMessage = L["Ledger unavailable. Please refresh and try again."];
            return;
        }
        _linkedLedgerEntries = await OptionalRpc.CallAsync<LinkedLedgerEntry[]>(_invoker!, methods,
            "ledger.by-calendar-entry", new
            {
                calendarEntryId = item.Id
            });
        _ledgerViewing = item;
    });
}
