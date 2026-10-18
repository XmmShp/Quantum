using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarTasksRequest(
    [property: Description("是否包含已完成待办。")] bool IncludeCompleted = true);
