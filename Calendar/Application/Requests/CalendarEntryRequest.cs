using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryRequest(
    [property: Description("日历事项 ID。")] Guid Id);
