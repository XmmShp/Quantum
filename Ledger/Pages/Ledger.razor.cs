using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using NOF.Contract;
using Quantum.OfficialPlugins.Ledger.Rpc;
using System.Globalization;
using System.Text;

namespace Quantum.OfficialPlugins.Ledger.Pages;

public partial class Ledger
{
    private readonly ILedgerRpcServiceClient _service;
    public Ledger(ILedgerRpcServiceClient service) => _service = service;
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private IReadOnlyList<LedgerEntryDetails> _entries = [];
    private bool _busy, _loaded, _editing;
    private string? _error, _message;
    private string _search = "", _filter = "all", _category = "", _note = "";
    private Guid? _editId;
    private Guid? _relatedExpenseId;
    private DateOnly _candidateMonth;
    private IReadOnlyList<LedgerEntryDetails> _candidates = [], _related = [];
    private LedgerEntryDetails? _linkedExpense, _viewing;
    private IEnumerable<LedgerEntryDetails> ExpenseOptions => _candidates.Concat(_linkedExpense is null ? [] : new[] { _linkedExpense })
        .Where(item => item.Id != _editId).DistinctBy(item => item.Id);
    private EntryKind _kind;
    private decimal _amount;
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Today);
    private LedgerEntryDetails? _deleting;
    private IReadOnlyList<LedgerEntryDetails>? _import;
    private string? _importCsv;
    private IJSObjectReference? _module;
    private static readonly string[] SuggestedCategories = ["Food", "Transport", "Shopping", "Housing", "Health", "Entertainment", "Salary", "Other"];
    private LedgerTotals Totals => TotalsFor(_entries);
    private LedgerTotals ImportTotals => TotalsFor(_import ?? []);
    private static LedgerTotals TotalsFor(IEnumerable<LedgerEntryDetails> entries) => new(
        entries.Where(item => item.Kind == EntryKind.Income).Sum(item => item.Amount),
        entries.Where(item => item.Kind == EntryKind.Expense).Sum(item => item.Amount));
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
        catch (RpcFailureException ex)
        {
            _error = L[ex.Message];
        }
        catch (ArgumentException ex)
        {
            _error = L[ex.Message];
        }
        catch (KeyNotFoundException ex)
        {
            _error = L[ex.Message];
        }
        catch (Exception)
        {
            _error = L["Operation failed. Please refresh and try again."];
        }
        finally
        {
            _busy = false;
        }
    }
    private async Task Load()
    {
        _entries = RpcResults.ValueOrThrow(await _service.ListEntriesAsync(new(_month), Context.Empty));
        _loaded = true;
    }
    private Task Refresh() => Run(Load);
    private async Task LoadMonth(DateOnly month)
    {
        var entries = RpcResults.ValueOrThrow(await _service.ListEntriesAsync(new(month), Context.Empty));
        _month = month;
        _entries = entries;
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
        await LoadCandidates();
        _relatedExpenseId = null;
        _linkedExpense = null;
        _editId = null;
        _kind = EntryKind.Expense;
        _amount = 0;
        _category = L["Food"];
        _note = "";
        _date = _month.Year == DateTime.Today.Year && _month.Month == DateTime.Today.Month ? DateOnly.FromDateTime(DateTime.Today) : _month;
        _error = null;
        _editing = true;
    });
    private Task Edit(LedgerEntryDetails item) => Run(async () =>
    {
        var linked = item.RelatedExpenseId is { } id ? RpcResults.ValueOrThrow(await _service.GetEntryAsync(new(id), Context.Empty)).Entry : null;
        _candidateMonth = linked is null ? _month : new(linked.Date.Year, linked.Date.Month, 1);
        await LoadCandidates();
        _relatedExpenseId = item.RelatedExpenseId;
        _linkedExpense = linked;
        _viewing = null;
        _editId = item.Id;
        _date = item.Date;
        _kind = item.Kind;
        _amount = item.Amount;
        _category = item.Category;
        _note = item.Note;
        _error = null;
        _editing = true;
    });
    private async Task LoadCandidates() => _candidates = (RpcResults.ValueOrThrow(await _service.ListEntriesAsync(new(_candidateMonth), Context.Empty))).Where(item => item.Kind == EntryKind.Expense).ToArray();
    private void RememberExpense() => _linkedExpense = ExpenseOptions.FirstOrDefault(item => item.Id == _relatedExpenseId);
    private Task SelectExpenseMonth(ChangeEventArgs args) => Run(async () =>
    {
        if (DateOnly.TryParseExact(args.Value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) && month.Year is >= 1900 and <= 9998)
        {
            _candidateMonth = month;
            await LoadCandidates();
        }
    });
    private Task ViewRelated(LedgerEntryDetails item) => Run(async () =>
    {
        _related = RpcResults.ValueOrThrow(await _service.GetRelatedEntriesAsync(new(item.Id), Context.Empty));
        _viewing = item;
    });
    private Task Save() => Run(async () =>
    {
        var request = new SaveLedgerEntryRequest(_date, _kind, _amount, _category, _note,
            _kind == EntryKind.Income ? _relatedExpenseId : null);
        RpcResults.ValueOrThrow(_editId is { } id
            ? await _service.UpdateEntryAsync(new(id, request), Context.Empty)
            : await _service.CreateEntryAsync(request, Context.Empty));
        _month = new(_date.Year, _date.Month, 1);
        _editing = false;
        await Load();
        _message = L["Entry saved."];
    });
    private Task Delete() => Run(async () =>
    {
        if (_deleting is null)
        {
            return;
        }

        RpcResults.ThrowIfFailed(await _service.DeleteEntryAsync(new(_deleting.Id), Context.Empty));
        _deleting = null;
        await Load();
        _message = L["Entry deleted."];
    });
    private Task Export() => Run(async () =>
    {
        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/quantum.plugin.ledger/ledger.js");
        var export = RpcResults.ValueOrThrow(await _service.ExportCsvAsync(new(_month), Context.Empty));
        await _module.InvokeVoidAsync("downloadCsv", export.FileName, export.Csv);
    });
    private Task ReadImport(InputFileChangeEventArgs args) => Run(async () =>
    {
        await using var stream = args.File.OpenReadStream(2 * 1024 * 1024);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        try
        {
            var csv = await reader.ReadToEndAsync();
            _import = LedgerCsv.Parse(csv).Select(LedgerEntryDetails.From).ToArray();
            _importCsv = csv;
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        {
            throw new ArgumentException("Invalid CSV. Export a month to obtain the template, then check IDs, dates, types and amounts.");
        }
    });
    private Task ConfirmImport() => Run(async () =>
    {
        if (_import is null || _importCsv is null)
        {
            return;
        }

        var added = RpcResults.ValueOrThrow(await _service.ImportCsvAsync(new(_importCsv), Context.Empty)).AddedCount;
        _import = null;
        _importCsv = null;
        await Load();
        _message = L["Imported {0} new records.", added];
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
