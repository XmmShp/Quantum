using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class UpdateLedgerEntry(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links, IDbContext database) : LedgerRpcService.UpdateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(UpdateLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            ArgumentNullException.ThrowIfNull(request.Entry);
            var replacement = request.Entry;
            _ = LedgerEntry.Create(request.Id, replacement.Date, replacement.Kind, replacement.Amount,
                replacement.Category, replacement.Note);
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Record no longer exists. Refresh and try again.");
            await LedgerQueries.ReplaceLinksAsync(entries, links, item.Id, replacement.RelatedEntryIds, cancellationToken);
            item.Update(replacement.Date, replacement.Kind, replacement.Amount, replacement.Category,
                replacement.Note);
            await database.SaveChangesAsync(cancellationToken);
            return (await LedgerQueries.DetailsAsync([item], links, cancellationToken))[0];
        });
}
