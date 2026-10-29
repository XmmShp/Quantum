using Microsoft.AspNetCore.Components;
using NOF.Contract;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.Plugin.Abstraction;
using System.Text.Json;

namespace Quantum.OfficialPlugins.Ledger.UI.Pages;

public partial class Ledger
{
    private readonly IRpcInvoker? _invoker;
    private bool _canListCalendar;
    private bool _canGetCalendar;
    private List<Guid> _selectedCalendarIds = [];
    private CalendarLinkDetails[] _calendarCandidates = [];
    private List<(Guid Id, CalendarLinkDetails? Entry)> _calendarLinks = [];
    private LedgerEntryDetails? _calendarViewing;
    private DateOnly _calendarMonth;

    private async Task RefreshCalendarCapabilitiesAsync()
    {
        _canListCalendar = false;
        _canGetCalendar = false;
        if (_invoker is null)
        {
            return;
        }
        try
        {
            var catalog = await _invoker.InvokeAsync<JsonElement>("quantum.rpc.catalog", new
            {
            }, Context.Empty);
            if (!catalog.IsSuccess)
            {
                return;
            }
            var capabilities = catalog.Value.GetProperty("services").EnumerateArray()
                .SelectMany(service => service.GetProperty("methods").EnumerateArray())
                .Select(method => method.GetProperty("canonicalName").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _canListCalendar = capabilities.Contains("calendar.list");
            _canGetCalendar = capabilities.Contains("calendar.get");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Optional UI actions remain hidden while the catalog is unavailable.
        }
    }

    private async Task LoadCalendarCandidates()
    {
        await RefreshCalendarCapabilitiesAsync();
        _calendarMonth = _candidateMonth;
        _calendarCandidates = [];
        if (!_canListCalendar)
        {
            return;
        }
        try
        {
            await QueryCalendarCandidates();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _error = L["Calendar unavailable. Saved links are preserved."];
        }
    }

    private async Task QueryCalendarCandidates()
    {
        var start = new DateOnly(_calendarMonth.Year, _calendarMonth.Month, 1);
        var result = await _invoker!.InvokeAsync<CalendarLinkDetails[]>(
            "calendar.list", new
            {
                startDate = start,
                endDate = start.AddMonths(1).AddDays(-1)
            }, Context.Empty);
        if (!IsSuccessful(result))
        {
            return;
        }
        _calendarCandidates = result.Value!;
    }

    private Task SelectCalendarMonth(ChangeEventArgs args) => Run(async () =>
    {
        if (DateOnly.TryParse(args.Value + "-01", out var month) && month.Year is >= 1900 and <= 9998)
        {
            _calendarMonth = month;
            await QueryCalendarCandidates();
        }
    });

    private void AddCalendarLink(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var id) && !_selectedCalendarIds.Contains(id))
        {
            _selectedCalendarIds.Add(id);
        }
    }

    private Task ViewCalendarLinks(LedgerEntryDetails item) => Run(async () =>
    {
        await RefreshCalendarCapabilitiesAsync();
        if (!_canGetCalendar)
        {
            _error = L["Calendar unavailable. Saved links are preserved."];
            return;
        }
        List<(Guid Id, CalendarLinkDetails? Entry)> links = [];
        foreach (var id in item.CalendarEntryIds ?? [])
        {
            var lookup = await _invoker!.InvokeAsync<CalendarLinkLookup>("calendar.get", new
            {
                id
            }, Context.Empty);
            if (!IsSuccessful(lookup))
            {
                return;
            }
            links.Add((id, lookup.Value!.Entry));
        }
        _calendarLinks = links;
        _calendarViewing = item;
    });
}
