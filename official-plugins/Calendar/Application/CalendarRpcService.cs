using System.ComponentModel;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarEntriesRequest(
    [property: Description("查询范围的起始日期，包含当天。")] DateOnly StartDate,
    [property: Description("查询范围的结束日期，包含当天。")] DateOnly EndDate,
    [property: Description("可选的事项类型筛选：Event 或 Task。")] CalendarEntryKind? Kind = null);

public sealed record ListCalendarTasksRequest(
    [property: Description("是否包含已完成待办。")] bool IncludeCompleted = true);

public sealed record CalendarEntryRequest(
    [property: Description("日历事项 ID。")] Guid Id);

public sealed record CalendarEntryLookup(
    [property: Description("找到的事项；不存在时为 null。")] CalendarEntryDetails? Entry);

public sealed record UpdateCalendarEntryRequest(
    [property: Description("要更新的日历事项 ID。")] Guid Id,
    [property: Description("事项的完整新内容。")] SaveCalendarEntryRequest Entry);

public sealed record SetCalendarEntryCompletedRequest(
    [property: Description("待办事项 ID。")] Guid Id,
    [property: Description("目标完成状态。")] bool IsCompleted);

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

public partial class CalendarRpcService : RpcServer<ICalendarRpcService>;
