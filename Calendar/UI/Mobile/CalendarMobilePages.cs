using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.UI.Localization;
using Quantum.Plugin.Abstraction;
using System.Globalization;
using System.Resources;
using Ui = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.OfficialPlugins.Calendar.UI.Mobile;

public sealed class CalendarMobilePages : ICalendarMobilePages
{
    private static readonly ResourceManager Resources = new(typeof(PluginStrings));
    private readonly ICalendarRpcServiceClient _client;
    private readonly IQuantumMobilePages? _pages;
    private readonly TimeProvider _clock;

    public CalendarMobilePages(ICalendarRpcServiceClient client, IQuantumMobilePages? pages = null, TimeProvider? timeProvider = null)
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
        var entries = await _client.ListEntriesAsync(new()
        {
            StartDate = today,
            EndDate = today
        }, Context.Empty, cancellationToken);
        var tasks = await _client.ListTasksAsync(new()
        {
            IncludeCompleted = false
        }, Context.Empty, cancellationToken);
        var nodes = new List<MobileNode>
        {
            Ui.Text("title", L("Calendar")).Appearance("title"),
            Ui.Text("date", L("Desktop date") + ": " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Ui.Text("refresh-help", L("After changes, prepare latest data, then use the phone's Refresh button.")).Appearance("caption"),
            Ui.Button("refresh", L("Prepare latest data")).OnClick(Ui.Rpc("quantum.plugin.calendar.calendar.mobile-refresh")),
            Ui.Text("today-title", L("Today's items")).Appearance("title")
        };
        if (entries.IsSuccess && tasks.IsSuccess)
        {
            var visible = entries.Value!.Take(20).Concat(tasks.Value!.Where(t => t.Date != today).Take(15)).ToArray();
            if (visible.Length == 0)
            {
                nodes.Add(Ui.Text("empty", L("No items to display.")));
            }
            foreach (var item in visible)
            {
                var id = item.Id.ToString("N");
                nodes.Add(Ui.Text("item-" + id, $"{item.Date:yyyy-MM-dd} {item.StartTime:HH:mm} · {item.Title}"));
                if (item.Kind == CalendarEntryKind.Task && !item.IsCompleted)
                {
                    nodes.Add(Ui.Button("complete-" + id, L("Mark complete")).OnClick(
                        Ui.Rpc("quantum.plugin.calendar.calendar.set-completed").Constant("id", item.Id.ToString())
                            .Constant("isCompleted", true)));
                }
            }
            if (entries.Value!.Length > 20 || tasks.Value!.Count(t => t.Date != today) > 15)
            {
                nodes.Add(Ui.Text("limited", L("Only a limited number of recent items are shown. Open the desktop for the full list.")).Appearance("caption"));
            }
        }
        else
        {
            nodes.Add(Ui.Text("load-error", L("Unable to load data. Prepare latest data to retry.")).Appearance("error"));
        }
        nodes.AddRange([
            Ui.Divider("form-divider"),
            Ui.Text("form-title", L("Add a task for today")).Appearance("title"),
            Ui.TextField("task-title").Hint(L("Title")).Limit(120),
            Ui.TextField("task-notes").Hint(L("Notes")).Limit(1000),
            Ui.Button("create", L("Add task")).OnClick(Ui.Rpc("quantum.plugin.calendar.calendar.create")
                .Input("title", "task-title").Input("notes", "task-notes")
                .Constant("kind", "Task").Constant("date", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Constant("startTime", "12:00:00").Constant("endTime", (string?)null).Constant("style", "violet")),
            Ui.Text("task-help", L("The task date is the displayed desktop date; its due time is 12:00. Refresh the data if the date has changed.")).Appearance("caption")
        ]);
        _pages.Publish(Ui.Page("calendar", L("Calendar"), Ui.Scroll("scroll", Ui.VStack("root", nodes.ToArray()))));
    }

    private static string L(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
