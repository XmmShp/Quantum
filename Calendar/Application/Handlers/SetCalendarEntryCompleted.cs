using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Localization;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class SetCalendarEntryCompleted(IRepository<CalendarEntry> entries, IDbContext database, TimeProvider timeProvider) : CalendarRpcService.SetCompleted
{
    public override Task<Result<CalendarEntryDetails>> HandleAsync(SetCalendarEntryCompletedRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(PluginText.Get("Item '{0}' was not found.", request.Id));
            item.SetCompleted(request.IsCompleted, timeProvider.GetUtcNow());
            await database.SaveChangesAsync(cancellationToken);
            return CalendarEntryDetails.From(item);
        });
}
