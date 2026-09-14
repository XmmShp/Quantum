using System.Text.Json.Serialization;

namespace Quantum.OfficialPlugins.Calendar.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<CalendarEntryKind>))]
public enum CalendarEntryKind
{
    Event = 0,
    Task = 1
}
