using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarTasksRequest
{
    [Description("Whether to include completed tasks.")]
    public bool IncludeCompleted { get; set; } = true;
}
