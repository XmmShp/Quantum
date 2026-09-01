using NOF.Application;
using NOF.Domain;
using Quantum.ExampleCalendarPlugin.Domain;

namespace Quantum.ExampleCalendarPlugin.Application;

internal sealed class CalendarItemApplicationService(
    IRepository<CalendarItem> calendarItems,
    IDbContext dbContext,
    TimeProvider timeProvider) : ICalendarItemApplicationService
{
    public async Task<IReadOnlyList<CalendarItemDetails>> ListAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), "结束日期不能早于开始日期。");
        }

        return await calendarItems.AsNoTracking()
            .Where(item => item.Date >= startDate && item.Date <= endDate)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.StartTime)
            .Select(item => ToDetails(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CalendarItemDetails?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await calendarItems.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => ToDetails(item))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CalendarItemDetails> CreateAsync(
        CreateCalendarItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = CalendarItem.Create(
            request.Title,
            request.Description,
            request.Date,
            request.StartTime,
            request.Style,
            timeProvider);
        await calendarItems.AddAsync(item, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDetails(item);
    }

    public async Task<CalendarItemDetails> UpdateAsync(
        Guid id,
        UpdateCalendarItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await calendarItems
            .Where(candidate => candidate.Id == id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"未找到事项 '{id}'。");
        item.Update(
            request.Title,
            request.Description,
            request.Date,
            request.StartTime,
            request.Style,
            timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDetails(item);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await calendarItems
            .Where(candidate => candidate.Id == id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"未找到事项 '{id}'。");
        calendarItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CalendarItemDetails ToDetails(CalendarItem item)
        => new(
            item.Id,
            item.Title,
            item.Description,
            item.Date,
            item.StartTime,
            item.Style);
}
