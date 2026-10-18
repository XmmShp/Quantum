using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class GetCalendarEntry(IRepository<CalendarEntry> entries) : CalendarRpcService.GetEntry
{
    public override Task<Result<CalendarEntryLookup>> HandleAsync(CalendarEntryRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            var item = await entries.AsNoTracking().Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken);
            return new CalendarEntryLookup(item is null ? null : CalendarEntryDetails.From(item));
        });
}
