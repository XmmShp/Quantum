using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public interface ICalendarApplicationService
{
    Task<IReadOnlyList<CalendarEntryDetails>> ListAsync(
        DateOnly startDate,
        DateOnly endDate,
        CalendarEntryKind? kind = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarEntryDetails>> ListTasksAsync(
        bool includeCompleted = true,
        CancellationToken cancellationToken = default);

    Task<CalendarEntryDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CalendarEntryDetails> CreateAsync(
        SaveCalendarEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<CalendarEntryDetails> UpdateAsync(
        Guid id,
        SaveCalendarEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<CalendarEntryDetails> SetCompletedAsync(
        Guid id,
        bool isCompleted,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
