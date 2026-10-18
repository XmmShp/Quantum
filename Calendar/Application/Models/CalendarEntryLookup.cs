using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryLookup(
    [property: Description("找到的事项；不存在时为 null。")] CalendarEntryDetails? Entry);
