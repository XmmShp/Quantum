using Quantum.OfficialPlugins.Calendar.Domain;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarEntriesRequest
{
    [Description("Inclusive start date.")]
    public required DateOnly StartDate { get; set; }

    [Description("Inclusive end date.")]
    public required DateOnly EndDate { get; set; }

    [Description("Optional item kind filter: Event or Task.")]
    public CalendarEntryKind? Kind { get; set; }
}
