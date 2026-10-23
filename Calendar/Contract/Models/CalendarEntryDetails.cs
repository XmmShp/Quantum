using Quantum.OfficialPlugins.Calendar.Domain;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryDetails
{
    [Description("Calendar item ID.")]
    public required Guid Id { get; set; }

    [Description("Calendar item title.")]
    public required string Title { get; set; }

    [Description("Calendar item notes.")]
    public required string Notes { get; set; }

    [Description("Item kind: Event or Task.")]
    public required CalendarEntryKind Kind { get; set; }

    [Description("Calendar item date.")]
    public required DateOnly Date { get; set; }

    [Description("Event start time or task due time.")]
    public required TimeOnly StartTime { get; set; }

    [Description("Event end time; null for tasks.")]
    public required TimeOnly? EndTime { get; set; }

    [Description("Whether the task is completed.")]
    public required bool IsCompleted { get; set; }

    [Description("UI color identifier.")]
    public required string Style { get; set; }
}
