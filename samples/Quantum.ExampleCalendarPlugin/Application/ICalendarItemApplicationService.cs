namespace Quantum.ExampleCalendarPlugin.Application;

public interface ICalendarItemApplicationService
{
    Task<IReadOnlyList<CalendarItemDetails>> ListAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<CalendarItemDetails?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CalendarItemDetails> CreateAsync(
        CreateCalendarItemRequest request,
        CancellationToken cancellationToken = default);

    Task<CalendarItemDetails> UpdateAsync(
        Guid id,
        UpdateCalendarItemRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
