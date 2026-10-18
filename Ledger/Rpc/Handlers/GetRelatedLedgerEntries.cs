using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetRelatedLedgerEntries(IRepository<LedgerEntry> entries) : LedgerRpcService.GetRelatedEntries
{
    public override Task<Result<LedgerEntryDetails[]>> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            var entry = await entries.AsNoTracking().Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Record no longer exists. Refresh and try again.");
            return (await entries.AsNoTracking().Where(item => item.Id == entry.RelatedExpenseId || item.RelatedExpenseId == request.Id)
                .OrderByDescending(item => item.Date).ThenBy(item => item.Id).ToArrayAsync(cancellationToken))
                .Select(LedgerEntryDetails.From).ToArray();
        });
}
