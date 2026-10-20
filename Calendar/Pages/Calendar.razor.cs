using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using NOF.Contract;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using System.Globalization;

namespace Quantum.OfficialPlugins.Calendar.Pages;

public partial class Calendar : IAsyncDisposable
{
    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    private ElementReference _editorDialog;
    private IJSObjectReference? _editorModule;
    private readonly string _editorTitleId = $"calendar-editor-{Guid.NewGuid():N}";
    private readonly ICalendarRpcServiceClient CalendarService;
    private static readonly string[] Styles = ["violet", "blue", "coral", "green"];

    public Calendar(ICalendarRpcServiceClient calendarService, Quantum.Plugin.Abstraction.IRpcInvoker? invoker = null)
    {
        CalendarService = calendarService;
        _invoker = invoker;
    }

    private static IReadOnlyList<string> Weekdays
    {
        get
        {
            var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            return [.. names.Skip(1), names[0]];
        }
    }

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private IReadOnlyList<CalendarEntryDetails> _items = [];
    private IReadOnlyList<CalendarEntryDetails> _tasks = [];
    private DateOnly _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private EntryDraft _draft = EntryDraft.Create(DateOnly.FromDateTime(DateTime.Today));
    private TaskFilter _taskFilter = TaskFilter.Open;
    private IReadOnlyList<CalendarDay>? _previousMonthDays;
    private int _monthDirection = 1;
    private Guid? _editingId;
    private (Guid Id, bool IsTaskList)? _pendingDelete;
    private bool _isEditorOpen;
    private bool _isBusy;
    private string? _errorMessage;

    private string MonthTitle => _visibleMonth.ToDateTime(TimeOnly.MinValue).ToString("Y", CultureInfo.CurrentCulture);
    private string SelectedDateTitle => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("MMMM dddd", CultureInfo.CurrentCulture);
    private string EditorTitle => _editingId is null ? L["New item"] : L["Edit item"];
    private IReadOnlyList<CalendarEntryDetails> SelectedItems => [.. _items.Where(item => item.Date == _selectedDate)];
    private int EventCount => _items.Count(item => item.Kind == CalendarEntryKind.Event);
    private int OpenTaskCount => _items.Count(item => item.Kind == CalendarEntryKind.Task && !item.IsCompleted);
    private int AllOpenTaskCount => _tasks.Count(task => !task.IsCompleted);
    private int FreeDayCount => DateTime.DaysInMonth(_visibleMonth.Year, _visibleMonth.Month) - _items.Select(item => item.Date).Distinct().Count();
    private IReadOnlyList<CalendarEntryDetails> FilteredTasks =>
        [.. _tasks.Where(task => _taskFilter switch
        {
            TaskFilter.Open => !task.IsCompleted,
            TaskFilter.Completed => task.IsCompleted,
            _ => true
        })];

    private IReadOnlyList<CalendarDay> CalendarDays
    {
        get
        {
            var firstDay = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, 1);
            var mondayOffset = ((int)firstDay.DayOfWeek + 6) % 7;
            var gridStart = firstDay.AddDays(-mondayOffset);
            return [.. Enumerable.Range(0, 42).Select(offset =>
            {
                var date = gridStart.AddDays(offset);
                return new CalendarDay(
                    date,
                    date.Month == _visibleMonth.Month && date.Year == _visibleMonth.Year,
                    date == _today,
                    [.. _items.Where(item => item.Date == date)]);
            })];
        }
    }

    protected override Task OnInitializedAsync() => ExecuteAsync(LoadAllCoreAsync);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_isEditorOpen && _ledgerViewing is null && _editorModule is null)
        {
            return;
        }

        _editorModule ??= await JavaScript.InvokeAsync<IJSObjectReference>(
            "import", "./_content/quantum.plugin.calendar/calendar.js");
        await _editorModule.InvokeVoidAsync("syncEditor", _editorDialog, _isEditorOpen);
        await _editorModule.InvokeVoidAsync("syncEditor", _ledgerDialog, _ledgerViewing is not null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_editorModule is null)
        {
            return;
        }

        try
        {
            await _editorModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The WebView has already disconnected.
        }
    }

    private void SetVisibleMonth(DateOnly month)
    {
        if (month == _visibleMonth)
        {
            return;
        }

        _previousMonthDays = CalendarDays;
        _monthDirection = month > _visibleMonth ? 1 : -1;
        _visibleMonth = month;
    }

    private async Task ChangeMonthAsync(int offset)
    {
        SetVisibleMonth(_visibleMonth.AddMonths(offset));
        _selectedDate = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, Math.Min(_selectedDate.Day, DateTime.DaysInMonth(_visibleMonth.Year, _visibleMonth.Month)));
        CancelEdit();
        await LoadMonthAsync();
    }

    private async Task ShowTodayAsync()
    {
        SetVisibleMonth(new DateOnly(_today.Year, _today.Month, 1));
        _selectedDate = _today;
        CancelEdit();
        await LoadMonthAsync();
    }

    private async Task SelectDateAsync(DateOnly date)
    {
        _selectedDate = date;
        CancelEdit();
        if (date.Month != _visibleMonth.Month || date.Year != _visibleMonth.Year)
        {
            SetVisibleMonth(new DateOnly(date.Year, date.Month, 1));
            await LoadMonthAsync();
        }
    }

    private void BeginCreate()
    {
        _editingId = null;
        _draft = EntryDraft.Create(_selectedDate);
        _isEditorOpen = true;
        _errorMessage = null;
    }

    private async Task BeginCreateTaskAsync()
    {
        _selectedDate = _today;
        SetVisibleMonth(new DateOnly(_today.Year, _today.Month, 1));
        await ExecuteAsync(LoadMonthCoreAsync);
        BeginCreate();
        SetDraftKind(CalendarEntryKind.Task);
    }

    private void BeginEdit(CalendarEntryDetails item)
    {
        _editingId = item.Id;
        _draft = EntryDraft.From(item);
        _isEditorOpen = true;
        _errorMessage = null;
    }

    private async Task EditTaskAsync(CalendarEntryDetails task)
    {
        _selectedDate = task.Date;
        SetVisibleMonth(new DateOnly(task.Date.Year, task.Date.Month, 1));
        await ExecuteAsync(LoadMonthCoreAsync);
        BeginEdit(task);
    }

    private void SetDraftKind(CalendarEntryKind kind)
    {
        _draft.Kind = kind;
        if (kind == CalendarEntryKind.Task)
        {
            _draft.EndTime = null;
        }
    }

    private void CancelEdit()
    {
        _editingId = null;
        _isEditorOpen = false;
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_draft.Title))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var request = _draft.ToRequest();
            if (_editingId is { } id)
            {
                RpcResults.ValueOrThrow(await CalendarService.UpdateEntryAsync(new(id, request), Context.Empty));
            }
            else
            {
                RpcResults.ValueOrThrow(await CalendarService.CreateEntryAsync(request, Context.Empty));
            }

            _selectedDate = _draft.Date;
            SetVisibleMonth(new DateOnly(_draft.Date.Year, _draft.Date.Month, 1));
            CancelEdit();
            await LoadAllCoreAsync();
        });
    }

    private Task ToggleCompletedAsync(CalendarEntryDetails item)
        => ExecuteAsync(async () =>
        {
            RpcResults.ValueOrThrow(await CalendarService.SetCompletedAsync(new(item.Id, !item.IsCompleted), Context.Empty));
            await LoadAllCoreAsync();
        });

    private void CancelDelete() => _pendingDelete = null;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CancelDelete();
        }
    }

    private Task DeleteAsync(Guid id, bool isTaskList)
    {
        if (_isBusy)
        {
            return Task.CompletedTask;
        }

        if (_pendingDelete != (id, isTaskList))
        {
            _pendingDelete = (id, isTaskList);
            return Task.CompletedTask;
        }

        CancelDelete();
        return ExecuteAsync(async () =>
        {
            RpcResults.ThrowIfFailed(await CalendarService.DeleteEntryAsync(new(id), Context.Empty));
            if (_editingId == id)
            {
                CancelEdit();
            }

            await LoadAllCoreAsync();
        });
    }

    private Task LoadMonthAsync() => ExecuteAsync(LoadMonthCoreAsync);

    private async Task LoadMonthCoreAsync()
    {
        var firstDay = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, 1);
        _items = RpcResults.ValueOrThrow(await CalendarService.ListEntriesAsync(new(firstDay, firstDay.AddMonths(1).AddDays(-1)), Context.Empty));
    }

    private async Task LoadAllCoreAsync()
    {
        await LoadMonthCoreAsync();
        _ledgerMethods = await OptionalRpc.DiscoverAsync(_invoker, "ledger.by-calendar-entry");
        _tasks = RpcResults.ValueOrThrow(await CalendarService.ListTasksAsync(new(), Context.Empty));
    }

    private bool IsOverdue(CalendarEntryDetails task)
        => !task.IsCompleted && (task.Date < _today || (task.Date == _today && task.StartTime < TimeOnly.FromDateTime(DateTime.Now)));

    private static string FormatDeadline(CalendarEntryDetails task)
        => $"{task.Date.ToDateTime(task.StartTime):D} · {task.StartTime:HH:mm}";

    private string FilterLabel(TaskFilter filter) => filter switch
    {
        TaskFilter.Open => L["Open"],
        TaskFilter.Completed => L["Completed"],
        _ => L["All"]
    };

    private async Task ExecuteAsync(Func<Task> action)
    {
        _isBusy = true;
        _errorMessage = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private string GetDayClass(CalendarDay day)
        => string.Join(" ", new[]
        {
            "day-cell",
            day.IsVisibleMonth ? null : "outside-month",
            day.IsToday ? "today" : null,
            day.Date == _selectedDate ? "selected" : null
        }.Where(value => value is not null));

    private string GetDayAriaLabel(CalendarDay day)
    {
        var date = day.Date.ToDateTime(TimeOnly.MinValue).ToString("D", CultureInfo.CurrentCulture);
        return L["{0}, {1} items", date, day.Items.Count];
    }

    private static string FormatTime(CalendarEntryDetails item)
        => item.EndTime is { } end
            ? $"{item.StartTime:HH:mm}–{end:HH:mm}"
            : item.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture);

    private sealed record CalendarDay(DateOnly Date, bool IsVisibleMonth, bool IsToday, IReadOnlyList<CalendarEntryDetails> Items);

    private enum TaskFilter
    {
        Open,
        Completed,
        All
    }

    private sealed class EntryDraft
    {
        public string Title { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public CalendarEntryKind Kind
        {
            get; set;
        }
        public DateOnly Date
        {
            get; set;
        }
        public TimeOnly StartTime
        {
            get; set;
        }
        public TimeOnly? EndTime
        {
            get; set;
        }
        public string Style { get; set; } = "violet";

        public SaveCalendarEntryRequest ToRequest() => new(Title, Notes, Kind, Date, StartTime, EndTime, Style);

        public static EntryDraft Create(DateOnly date) => new() { Date = date, StartTime = new TimeOnly(9, 0) };

        public static EntryDraft From(CalendarEntryDetails item) => new()
        {
            Title = item.Title,
            Notes = item.Notes,
            Kind = item.Kind,
            Date = item.Date,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            Style = item.Style
        };
    }
}
