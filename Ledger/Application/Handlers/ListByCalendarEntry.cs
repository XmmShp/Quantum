using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ListByCalendarEntry(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links) : LedgerRpcService.ListByCalendarEntry
{
    public override async Task<Result<LedgerEntryDetails[]>> HandleAsync(CalendarLedgerRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LedgerQueries.ValidateId(request.CalendarEntryId);
        var token = request.CalendarEntryId.ToString();
        var matching = entries.AsNoTracking().Where(item => item.CalendarEntryIdsJson.Contains(token))
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id);
        return await LedgerQueries.DetailsAsync(matching, links, cancellationToken);
    }
}
