using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class CreateCalendarEntry(IRepository<CalendarEntry> entries, IDbContext database, TimeProvider timeProvider) : CalendarRpcService.CreateEntry
{
    public override async Task<Result<CalendarEntryDetails>> HandleAsync(SaveCalendarEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var item = CalendarEntry.Create(request.Title, request.Notes, request.Kind, request.Date,
            request.StartTime, request.EndTime, request.Style, timeProvider);
        await entries.AddAsync(item, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return Mapper.Current.Map<CalendarEntry, CalendarEntryDetails>(item);
    }
}
