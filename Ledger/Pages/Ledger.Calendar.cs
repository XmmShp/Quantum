using Microsoft.AspNetCore.Components;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Ledger.Pages;

public partial class Ledger
{
    private readonly IRpcInvoker? _invoker;
    private Dictionary<string, string> _calendarMethods = [];
    private List<Guid> _selectedCalendarIds = [];
    private CalendarLinkDetails[] _calendarCandidates = [];
    private List<(Guid Id, CalendarLinkDetails? Entry)> _calendarLinks = [];
    private LedgerEntryDetails? _calendarViewing;
    private DateOnly _calendarMonth;

    private async Task LoadCalendarCandidates()
    {
        _calendarMethods = await OptionalRpc.DiscoverAsync(_invoker, "calendar.list", "calendar.get");
        _calendarMonth = _candidateMonth;
        _calendarCandidates = [];
        if (_calendarMethods.Count == 0)
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
        _calendarCandidates = await OptionalRpc.CallAsync<CalendarLinkDetails[]>(_invoker!, _calendarMethods,
            "calendar.list", new
            {
                startDate = start,
                endDate = start.AddMonths(1).AddDays(-1)
            });
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
        _calendarMethods = await OptionalRpc.DiscoverAsync(_invoker, "calendar.list", "calendar.get");
        if (_calendarMethods.Count == 0)
        {
            _error = L["Calendar unavailable. Saved links are preserved."];
            return;
        }
        _calendarLinks = [];
        foreach (var id in item.CalendarEntryIds ?? [])
        {
            var lookup = await OptionalRpc.CallAsync<CalendarLinkLookup>(_invoker!, _calendarMethods, "calendar.get", new
            {
                id
            });
            _calendarLinks.Add((id, lookup.Entry));
        }
        _calendarViewing = item;
    });
}
