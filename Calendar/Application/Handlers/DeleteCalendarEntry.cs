using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Localization;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class DeleteCalendarEntry(IRepository<CalendarEntry> entries, IDbContext database) : CalendarRpcService.DeleteEntry
{
    public override Task<Result> HandleAsync(CalendarEntryRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(PluginText.Get("Item '{0}' was not found.", request.Id));
            entries.Remove(item);
            await database.SaveChangesAsync(cancellationToken);
        });
}
