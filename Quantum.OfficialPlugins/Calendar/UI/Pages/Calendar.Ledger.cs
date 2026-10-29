using Microsoft.AspNetCore.Components;
using NOF.Contract;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.Plugin.Abstraction;
using System.Text.Json;

namespace Quantum.OfficialPlugins.Calendar.UI.Pages;

public partial class Calendar
{
    private readonly IRpcInvoker? _invoker;
    private bool _canViewLedger;
    private CalendarEntryDetails? _ledgerViewing;
    private LinkedLedgerEntry[] _linkedLedgerEntries = [];
    private ElementReference _ledgerDialog;

    private async Task RefreshLedgerCapabilityAsync()
    {
        _canViewLedger = false;
        if (_invoker is null)
        {
            return;
        }
        try
        {
            var catalog = await _invoker.InvokeAsync<JsonElement>("quantum.rpc.catalog", new
            {
            }, Context.Empty);
            _canViewLedger = catalog.IsSuccess && catalog.Value.GetProperty("services").EnumerateArray()
                .SelectMany(service => service.GetProperty("methods").EnumerateArray())
                .Any(method => string.Equals(method.GetProperty("canonicalName").GetString(), "ledger.by-calendar-entry", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Optional UI actions remain hidden while the catalog is unavailable.
        }
    }

    private Task ViewLedgerAsync(CalendarEntryDetails item) => ExecuteAsync(async () =>
    {
        await RefreshLedgerCapabilityAsync();
        if (!_canViewLedger)
        {
            _errorMessage = L["Ledger unavailable. Please refresh and try again."];
            return;
        }
        var result = await _invoker!.InvokeAsync<LinkedLedgerEntry[]>(
            "ledger.by-calendar-entry", new
            {
                calendarEntryId = item.Id
            }, Context.Empty);
        if (!IsSuccessful(result))
        {
            return;
        }
        _linkedLedgerEntries = result.Value!;
        _ledgerViewing = item;
    });
}
