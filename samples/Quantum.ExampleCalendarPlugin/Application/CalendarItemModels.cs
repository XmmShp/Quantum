namespace Quantum.ExampleCalendarPlugin.Application;

public sealed record CalendarItemDetails(
    Guid Id,
    string Title,
    string Description,
    DateOnly Date,
    TimeOnly StartTime,
    string Style);

public sealed record CreateCalendarItemRequest(
    string Title,
    string Description,
    DateOnly Date,
    TimeOnly StartTime,
    string Style);

public sealed record UpdateCalendarItemRequest(
    string Title,
    string Description,
    DateOnly Date,
    TimeOnly StartTime,
    string Style);
