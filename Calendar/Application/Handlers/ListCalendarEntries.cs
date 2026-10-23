using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class ListCalendarEntries(IRepository<CalendarEntry> entries) : CalendarRpcService.ListEntries
{
    public override async Task<Result<CalendarEntryDetails[]>> HandleAsync(ListCalendarEntriesRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.EndDate < request.StartDate)
        {
            throw new DomainException("calendar_invalid_request", "End date cannot be earlier than start date.");
        }

        var query = entries.AsNoTracking().Where(item => item.Date >= request.StartDate && item.Date <= request.EndDate);
        if (request.Kind is not null)
        {
            query = query.Where(item => item.Kind == request.Kind);
        }

        return await query.OrderBy(item => item.Date).ThenBy(item => item.StartTime).ThenBy(item => item.Title)
            .ProjectTo<CalendarEntryDetails>().ToArrayAsync(cancellationToken);
    }
}
