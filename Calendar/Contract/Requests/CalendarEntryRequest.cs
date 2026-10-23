using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryRequest
{
    [Description("Calendar item ID.")]
    public required Guid Id { get; set; }
}
