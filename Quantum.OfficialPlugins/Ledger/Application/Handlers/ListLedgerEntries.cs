using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ListLedgerEntries(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links) : LedgerRpcService.ListEntries
{
    public override async Task<Result<LedgerEntryDetails[]>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await LedgerQueries.DetailsAsync(LedgerQueries.Month(entries, request.Month), links, cancellationToken);
    }
}
