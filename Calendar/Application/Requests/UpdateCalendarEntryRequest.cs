using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record UpdateCalendarEntryRequest(
    [property: Description("要更新的日历事项 ID。")] Guid Id,
    [property: Description("事项的完整新内容。")] SaveCalendarEntryRequest Entry);
