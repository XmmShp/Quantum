using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record UpdateCalendarEntryRequest
{
    [Description("ID of the calendar item to update.")]
    public required Guid Id { get; set; }

    [Description("The complete replacement contents of the calendar item.")]
    public required SaveCalendarEntryRequest Entry { get; set; }
}
