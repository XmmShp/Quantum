using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Localization;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class ListCalendarEntries(IRepository<CalendarEntry> entries) : CalendarRpcService.ListEntries
{
    public override Task<Result<CalendarEntryDetails[]>> HandleAsync(ListCalendarEntriesRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            if (request.EndDate < request.StartDate)
            {
                throw new ArgumentOutOfRangeException(nameof(request.EndDate), PluginText.Get("End date cannot be earlier than start date."));
            }

            var query = entries.AsNoTracking().Where(item => item.Date >= request.StartDate && item.Date <= request.EndDate);
            if (request.Kind is not null)
            {
                query = query.Where(item => item.Kind == request.Kind);
            }

            return (await query.OrderBy(item => item.Date).ThenBy(item => item.StartTime).ThenBy(item => item.Title)
                .ToArrayAsync(cancellationToken)).Select(CalendarEntryDetails.From).ToArray();
        });
}
