using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class CreateLedgerEntry(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links, IDbContext database) : LedgerRpcService.CreateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(SaveLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            var item = LedgerEntry.Create(Guid.NewGuid(), request.Date, request.Kind, request.Amount,
                request.Category, request.Note);
            item.SetCalendarEntries(request.CalendarEntryIds);
            await LedgerQueries.ReplaceLinksAsync(entries, links, item.Id, request.RelatedEntryIds, cancellationToken);
            await entries.AddAsync(item, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return (await LedgerQueries.DetailsAsync([item], links, cancellationToken))[0];
        });
}
