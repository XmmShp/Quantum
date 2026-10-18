using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Localization;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class UpdateCalendarEntry(IRepository<CalendarEntry> entries, IDbContext database, TimeProvider timeProvider) : CalendarRpcService.UpdateEntry
{
    public override Task<Result<CalendarEntryDetails>> HandleAsync(UpdateCalendarEntryRequest request, Context context, CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request.Entry);
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(PluginText.Get("Item '{0}' was not found.", request.Id));
            var replacement = request.Entry;
            item.Update(replacement.Title, replacement.Notes, replacement.Kind, replacement.Date,
                replacement.StartTime, replacement.EndTime, replacement.Style, timeProvider.GetUtcNow());
            await database.SaveChangesAsync(cancellationToken);
            return CalendarEntryDetails.From(item);
        });
}
