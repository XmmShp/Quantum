using NOF.Application;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Localization;

namespace Quantum.OfficialPlugins.Calendar.Application;

internal sealed class CalendarApplicationService(
    IRepository<CalendarEntry> entries,
    IDbContext dbContext,
    TimeProvider timeProvider) : ICalendarApplicationService
{
    public async Task<IReadOnlyList<CalendarEntryDetails>> ListAsync(
        DateOnly startDate,
        DateOnly endDate,
        CalendarEntryKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                PluginText.Get("结束日期不能早于开始日期。"));
        }

        var query = entries.AsNoTracking()
            .Where(item => item.Date >= startDate && item.Date <= endDate);
        if (kind is not null)
        {
            query = query.Where(item => item.Kind == kind);
        }

        return await query
            .OrderBy(item => item.Date)
            .ThenBy(item => item.StartTime)
            .ThenBy(item => item.Title)
            .Select(item => ToDetails(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEntryDetails>> ListTasksAsync(
        bool includeCompleted = true,
        CancellationToken cancellationToken = default)
    {
        var query = entries.AsNoTracking().Where(item => item.Kind == CalendarEntryKind.Task);
        if (!includeCompleted)
        {
            query = query.Where(item => !item.IsCompleted);
        }

        return await query
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.Date)
            .ThenBy(item => item.StartTime)
            .ThenBy(item => item.Title)
            .Select(item => ToDetails(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CalendarEntryDetails?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await entries.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => ToDetails(item))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CalendarEntryDetails> CreateAsync(
        SaveCalendarEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = CalendarEntry.Create(
            request.Title,
            request.Notes,
            request.Kind,
            request.Date,
            request.StartTime,
            request.EndTime,
            request.Style,
            timeProvider);
        await entries.AddAsync(item, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDetails(item);
    }

    public async Task<CalendarEntryDetails> UpdateAsync(
        Guid id,
        SaveCalendarEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await FindRequiredAsync(id, cancellationToken);
        item.Update(
            request.Title,
            request.Notes,
            request.Kind,
            request.Date,
            request.StartTime,
            request.EndTime,
            request.Style,
            timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDetails(item);
    }

    public async Task<CalendarEntryDetails> SetCompletedAsync(
        Guid id,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        var item = await FindRequiredAsync(id, cancellationToken);
        item.SetCompleted(isCompleted, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDetails(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await FindRequiredAsync(id, cancellationToken);
        entries.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CalendarEntry> FindRequiredAsync(Guid id, CancellationToken cancellationToken)
        => await entries.Where(item => item.Id == id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(PluginText.Get("未找到事项 '{0}'。", id));

    private static CalendarEntryDetails ToDetails(CalendarEntry item)
        => new(
            item.Id,
            item.Title,
            item.Notes,
            item.Kind,
            item.Date,
            item.StartTime,
            item.EndTime,
            item.IsCompleted,
            item.Style);
}
