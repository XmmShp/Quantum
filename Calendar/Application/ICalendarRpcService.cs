using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

[Description("管理 Quantum 官方日历中的日程与待办事项。")]
[TransportOverQuantum]
[RpcInvocationName("calendar")]
public interface ICalendarRpcService : IRpcService
{
    [Description("列出指定日期范围内的日程和待办。")]
    [RpcInvocationName("list")]
    Result<CalendarEntryDetails[]> ListEntries(ListCalendarEntriesRequest request);

    [Description("列出全部待办，可选择排除已完成项。")]
    [RpcInvocationName("list-tasks")]
    Result<CalendarEntryDetails[]> ListTasks(ListCalendarTasksRequest request);

    [Description("按 ID 获取一个日历事项。")]
    [RpcInvocationName("get")]
    Result<CalendarEntryLookup> GetEntry(CalendarEntryRequest request);

    [Description("创建日程或待办事项。")]
    [RpcInvocationName("create")]
    Result<CalendarEntryDetails> CreateEntry(SaveCalendarEntryRequest request);

    [Description("替换指定日历事项的内容。")]
    [RpcInvocationName("update")]
    Result<CalendarEntryDetails> UpdateEntry(UpdateCalendarEntryRequest request);

    [Description("设置待办事项的完成状态。")]
    [RpcInvocationName("set-completed")]
    Result<CalendarEntryDetails> SetCompleted(SetCalendarEntryCompletedRequest request);

    [Description("永久删除指定日历事项。")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(CalendarEntryRequest request);
}
