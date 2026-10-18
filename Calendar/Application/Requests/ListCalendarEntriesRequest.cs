using Quantum.OfficialPlugins.Calendar.Domain;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarEntriesRequest(
    [property: Description("查询范围的起始日期，包含当天。")] DateOnly StartDate,
    [property: Description("查询范围的结束日期，包含当天。")] DateOnly EndDate,
    [property: Description("可选的事项类型筛选：Event 或 Task。")] CalendarEntryKind? Kind = null);
