using Quantum.OfficialPlugins.Calendar.Domain;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record SaveCalendarEntryRequest
{
    [Description("Calendar item title, up to 120 characters.")]
    public required string Title { get; set; }

    [Description("Calendar item notes, up to 1,000 characters.")]
    public required string Notes { get; set; }

    [Description("Item kind: Event or Task.")]
    public required CalendarEntryKind Kind { get; set; }

    [Description("Calendar item date.")]
    public required DateOnly Date { get; set; }

    [Description("Event start time or task due time.")]
    public required TimeOnly StartTime { get; set; }

    [Description("Event end time; must be null for tasks.")]
    public required TimeOnly? EndTime { get; set; }

    [Description("Color identifier: violet, blue, coral, or green.")]
    public required string Style { get; set; }
}
