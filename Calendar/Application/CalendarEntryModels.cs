using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record CalendarEntryDetails(
    Guid Id,
    string Title,
    string Notes,
    CalendarEntryKind Kind,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly? EndTime,
    bool IsCompleted,
    string Style);

public sealed record SaveCalendarEntryRequest(
    string Title,
    string Notes,
    CalendarEntryKind Kind,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly? EndTime,
    string Style);
