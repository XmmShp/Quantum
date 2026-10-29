using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class GetCalendarEntry(IRepository<CalendarEntry> entries) : CalendarRpcService.GetEntry
{
    public override async Task<Result<CalendarEntryLookup>> HandleAsync(CalendarEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await entries.AsNoTracking().Where(item => item.Id == request.Id).ProjectTo<CalendarEntryDetails>().SingleOrDefaultAsync(cancellationToken);
        return new CalendarEntryLookup
        {
            Entry = item
        };
    }
}
