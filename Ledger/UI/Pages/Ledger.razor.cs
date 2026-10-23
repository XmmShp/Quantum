using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using NOF.Contract;
using Quantum.OfficialPlugins.Ledger.Rpc;
using System.Globalization;

namespace Quantum.OfficialPlugins.Ledger.UI.Pages;

public partial class Ledger
{
    private readonly ILedgerRpcServiceClient _service;
    public Ledger(ILedgerRpcServiceClient service, Quantum.Plugin.Abstraction.IRpcInvoker? invoker = null)
    {
        _service = service;
        _invoker = invoker;
    }

    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private IReadOnlyList<LedgerEntryDetails> _entries = [];
    private bool _busy, _loaded, _editing;
    private string? _error, _message;
    private string _search = "", _filter = "all", _category = "", _note = "";
    private Guid? _editId;
    private DateOnly _candidateMonth;
    private IReadOnlyList<LedgerEntryDetails> _candidates = [], _related = [];
    private LedgerEntryDetails? _viewing;
    private List<LedgerEntryDetails> _selectedRelated = [];
    private IEnumerable<LedgerEntryDetails> RelationOptions => _candidates
        .Where(item => item.Id != _editId && !_selectedRelated.Any(selected => selected.Id == item.Id));
    private EntryKind _kind;
    private decimal _amount;
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Today);
    private LedgerEntryDetails? _deleting;
    private IJSObjectReference? _module;
    private ElementReference _dialog;
    private bool IsDialogOpen => _editing || _viewing is not null || _calendarViewing is not null;
    private string DialogTitle => L[_editing ? (_editId is null ? "Add entry" : "Edit entry") : (_calendarViewing is null ? "Related entries" : "Linked calendar items")];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!IsDialogOpen && _module is null)
        {
            return;
        }

        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/quantum.plugin.ledger/ledger.js");
        await _module.InvokeVoidAsync("syncDialog", _dialog, IsDialogOpen);
    }

    private void CloseDialog()
    {
        if (_busy)
        {
            return;
        }

        _editing = false;
        _calendarViewing = null;
        _viewing = null;
        _error = null;
    }

    private void CancelDelete() => _deleting = null;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CancelDelete();
        }
    }

    private Task RequestDelete(LedgerEntryDetails entry)
    {
        if (_busy)
        {
            return Task.CompletedTask;
        }

        if (_deleting?.Id != entry.Id)
        {
            _deleting = entry;
            return Task.CompletedTask;
        }

        return Delete();
    }

    private static readonly string[] SuggestedCategories = ["Food", "Transport", "Shopping", "Housing", "Health", "Entertainment", "Salary", "Other"];
    private LedgerTotals Totals => TotalsFor(_entries);
    private static LedgerTotals TotalsFor(IEnumerable<LedgerEntryDetails> entries) => new()
    {
        Income = entries.Where(item => item.Kind == EntryKind.Income).Sum(item => item.Amount),
        Expense = entries.Where(item => item.Kind == EntryKind.Expense).Sum(item => item.Amount)
    };
    private IReadOnlyList<LedgerEntryDetails> Filtered => _entries.Where(item =>
        (_filter == "all" || (_filter == "income") == (item.Kind == EntryKind.Income)) &&
        (item.Category.Contains(_search, StringComparison.CurrentCultureIgnoreCase) || item.Note.Contains(_search, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    private (string Name, decimal Amount)[] Categories => _entries.Where(item => item.Kind == EntryKind.Expense)
        .GroupBy(item => item.Category).Select(group => (Name: group.Key, Amount: group.Sum(item => item.Amount)))
        .OrderByDescending(item => item.Amount).ThenBy(item => item.Name).ToArray();
    private decimal Percent(decimal amount) => Totals.Expense == 0 ? 0 : amount / Totals.Expense * 100;
    private static string Money(decimal value) => "¥" + value.ToString("N2", CultureInfo.CurrentCulture);
    protected override Task OnInitializedAsync() => Refresh();

    private async Task Run(Func<Task> operation)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        _message = null;
        try
        {
            await operation();
        }
        catch (ArgumentException ex)
        {
            _error = L[ex.Message];
        }
        catch (KeyNotFoundException ex)
        {
            _error = L[ex.Message];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _error = L["Operation failed. Please refresh and try again."];
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<bool> Load()
    {
        var result = await _service.ListEntriesAsync(new()
        {
            Month = _month
        }, Context.Empty);
        if (!IsSuccessful(result))
        {
            return false;
        }
        _entries = result.Value!;
        _loaded = true;
        return true;
    }

    private bool IsSuccessful(IResult result)
    {
        if (result.IsSuccess)
        {
            return true;
        }
        _error = L[result.Message];
        return false;
    }

    private Task Refresh() => Run(async () =>
    {
        if (!await Load())
        {
            return;
        }
        await RefreshCalendarCapabilitiesAsync();
    });
    private async Task LoadMonth(DateOnly month)
    {
        var entries = await _service.ListEntriesAsync(new()
        {
            Month = month
        }, Context.Empty);
        if (!IsSuccessful(entries))
        {
            return;
        }
        _month = month;
        _entries = entries.Value!;
        _loaded = true;
    }

    private Task ChangeMonth(int offset) => Run(() => LoadMonth(_month.AddMonths(offset)));
    private Task SelectMonth(ChangeEventArgs args) => Run(async () =>
    {
        if (DateOnly.TryParseExact(args.Value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) && month.Year is >= 1900 and <= 9998)
        {
            await LoadMonth(month);
        }
    });
    private Task NewEntry() => Run(async () =>
    {
        _candidateMonth = _month;
        if (!await LoadCandidates())
        {
            return;
        }
        _selectedCalendarIds = [];
        await LoadCalendarCandidates();
        _selectedRelated = [];
        _editId = null;
        _kind = EntryKind.Expense;
        _amount = 0;
        _category = L["Food"];
        _note = "";
        _date = _month.Year == DateTime.Today.Year && _month.Month == DateTime.Today.Month ? DateOnly.FromDateTime(DateTime.Today) : _month;
        _calendarViewing = null;
        _editing = true;
    });
    private Task Edit(LedgerEntryDetails item) => Run(async () =>
    {
        var related = await _service.GetRelatedEntriesAsync(new()
        {
            Id = item.Id
        }, Context.Empty);
        if (!IsSuccessful(related))
        {
            return;
        }
        _candidateMonth = _month;
        if (!await LoadCandidates())
        {
            return;
        }
        _selectedCalendarIds = [.. item.CalendarEntryIds ?? []];
        await LoadCalendarCandidates();
        _selectedRelated = [.. related.Value!];
        _viewing = null;
        _editId = item.Id;
        _date = item.Date;
        _kind = item.Kind;
        _amount = item.Amount;
        _category = item.Category;
        _note = item.Note;
        _calendarViewing = null;
        _editing = true;
    });
    private async Task<bool> LoadCandidates()
    {
        var result = await _service.ListEntriesAsync(new()
        {
            Month = _candidateMonth
        }, Context.Empty);
        if (!IsSuccessful(result))
        {
            return false;
        }
        _candidates = result.Value!;
        return true;
    }

    private void AddRelatedEntry(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var id) && RelationOptions.FirstOrDefault(item => item.Id == id) is { } entry)
        {
            _selectedRelated.Add(entry);
        }
    }

    private Task SelectEntryMonth(ChangeEventArgs args) => Run(async () =>
    {
        if (DateOnly.TryParseExact(args.Value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) && month.Year is >= 1900 and <= 9998)
        {
            _candidateMonth = month;
            if (!await LoadCandidates())
            {
                return;
            }
        }
    });
    private Task ViewRelated(LedgerEntryDetails item) => Run(async () =>
    {
        var result = await _service.GetRelatedEntriesAsync(new()
        {
            Id = item.Id
        }, Context.Empty);
        if (!IsSuccessful(result))
        {
            return;
        }
        _related = result.Value!;
        _viewing = item;
    });
    private Task Save() => Run(async () =>
    {
        var request = new SaveLedgerEntryRequest
        {
            Date = _date,
            Kind = _kind,
            Amount = _amount,
            Category = _category,
            Note = _note,
            RelatedEntryIds = _selectedRelated.Select(item => item.Id).ToArray(),
            CalendarEntryIds = _selectedCalendarIds.ToArray()
        };
        if (!IsSuccessful(_editId is { } id
            ? await _service.UpdateEntryAsync(new()
            {
                Id = id,
                Entry = request
            }, Context.Empty)
            : await _service.CreateEntryAsync(request, Context.Empty)))
        {
            return;
        }
        _month = new(_date.Year, _date.Month, 1);
        _editing = false;
        if (!await Load())
        {
            return;
        }
        _message = L["Entry saved."];
    });
    private Task Delete() => Run(async () =>
    {
        if (_deleting is null)
        {
            return;
        }

        if (!IsSuccessful(await _service.DeleteEntryAsync(new()
        {
            Id = _deleting.Id
        }, Context.Empty)))
        {
            return;
        }
        _deleting = null;
        if (!await Load())
        {
            return;
        }
        _message = L["Entry deleted."];
    });
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
