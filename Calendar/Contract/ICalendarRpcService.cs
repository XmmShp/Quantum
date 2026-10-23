using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

[Description("Manage events and tasks in the official Quantum calendar.")]
[TransportOverQuantum]
[RpcInvocationName("calendar")]
public interface ICalendarRpcService : IRpcService
{
    [Description("List events and tasks within the inclusive date range.")]
    [RpcInvocationName("list")]
    Result<CalendarEntryDetails[]> ListEntries(ListCalendarEntriesRequest request);

    [Description("List all tasks, optionally excluding completed tasks.")]
    [RpcInvocationName("list-tasks")]
    Result<CalendarEntryDetails[]> ListTasks(ListCalendarTasksRequest request);

    [Description("Get a calendar item by ID.")]
    [RpcInvocationName("get")]
    Result<CalendarEntryLookup> GetEntry(CalendarEntryRequest request);

    [Description("Create an event or task.")]
    [RpcInvocationName("create")]
    Result<CalendarEntryDetails> CreateEntry(SaveCalendarEntryRequest request);

    [Description("Replace the contents of a calendar item.")]
    [RpcInvocationName("update")]
    Result<CalendarEntryDetails> UpdateEntry(UpdateCalendarEntryRequest request);

    [Description("Set the completion state of a task.")]
    [RpcInvocationName("set-completed")]
    Result<CalendarEntryDetails> SetCompleted(SetCalendarEntryCompletedRequest request);

    [Description("Permanently delete a calendar item.")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(CalendarEntryRequest request);
}
