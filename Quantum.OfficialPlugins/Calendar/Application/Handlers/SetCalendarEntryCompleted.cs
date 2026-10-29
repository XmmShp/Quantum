using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class SetCalendarEntryCompleted(IRepository<CalendarEntry> entries, IDbContext database, TimeProvider timeProvider) : CalendarRpcService.SetCompleted
{
    public override async Task<Result<CalendarEntryDetails>> HandleAsync(SetCalendarEntryCompletedRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("calendar_entry_not_found", "The calendar item was not found.");
        item.SetCompleted(request.IsCompleted, timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        return Mapper.Current.Map<CalendarEntry, CalendarEntryDetails>(item);
    }
}
