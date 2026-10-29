using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryLookup
{
    [Description("The matching calendar item, or null if it does not exist.")]
    public required CalendarEntryDetails? Entry { get; set; }
}
