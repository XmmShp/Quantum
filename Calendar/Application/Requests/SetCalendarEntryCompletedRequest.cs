using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record SetCalendarEntryCompletedRequest(
    [property: Description("待办事项 ID。")] Guid Id,
    [property: Description("目标完成状态。")] bool IsCompleted);
