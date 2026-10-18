using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class ListCalendarTasks(IRepository<CalendarEntry> entries) : CalendarRpcService.ListTasks
{
    public override Task<Result<CalendarEntryDetails[]>> HandleAsync(ListCalendarTasksRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            var query = entries.AsNoTracking().Where(item => item.Kind == CalendarEntryKind.Task);
            if (!request.IncludeCompleted)
            {
                query = query.Where(item => !item.IsCompleted);
            }

            return (await query.OrderBy(item => item.IsCompleted).ThenBy(item => item.Date).ThenBy(item => item.StartTime)
                .ThenBy(item => item.Title).ToArrayAsync(cancellationToken)).Select(CalendarEntryDetails.From).ToArray();
        });
}
