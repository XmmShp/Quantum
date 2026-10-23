using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record SetCalendarEntryCompletedRequest
{
    [Description("Task ID.")]
    public required Guid Id { get; set; }

    [Description("The desired completion state.")]
    public required bool IsCompleted { get; set; }
}
