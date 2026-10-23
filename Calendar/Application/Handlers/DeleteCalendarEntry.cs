using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class DeleteCalendarEntry(IRepository<CalendarEntry> entries, IDbContext database) : CalendarRpcService.DeleteEntry
{
    public override async Task<Result> HandleAsync(CalendarEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("calendar_entry_not_found", "The calendar item was not found.");
        entries.Remove(item);
        await database.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
