using System.ComponentModel;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryDetails(
    [property: Description("事项 ID。")] Guid Id,
    [property: Description("事项标题。")] string Title,
    [property: Description("事项备注。")] string Notes,
    [property: Description("事项类型：Event 或 Task。")] CalendarEntryKind Kind,
    [property: Description("事项日期。")] DateOnly Date,
    [property: Description("开始时间或待办截止时间。")] TimeOnly StartTime,
    [property: Description("日程结束时间；待办为 null。")] TimeOnly? EndTime,
    [property: Description("待办是否已完成。")] bool IsCompleted,
    [property: Description("界面颜色标识。")] string Style);

public sealed record SaveCalendarEntryRequest(
    [property: Description("事项标题，最长 120 个字符。")] string Title,
    [property: Description("事项备注，最长 1000 个字符。")] string Notes,
    [property: Description("事项类型：Event 或 Task。")] CalendarEntryKind Kind,
    [property: Description("事项日期。")] DateOnly Date,
    [property: Description("开始时间或待办截止时间。")] TimeOnly StartTime,
    [property: Description("日程结束时间；待办应为 null。")] TimeOnly? EndTime,
    [property: Description("颜色标识：violet、blue、coral 或 green。")] string Style);
