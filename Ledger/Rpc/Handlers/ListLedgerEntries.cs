using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ListLedgerEntries(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links) : LedgerRpcService.ListEntries
{
    public override Task<Result<LedgerEntryDetails[]>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
            await LedgerQueries.DetailsAsync(await LedgerQueries.Month(entries, request.Month).ToArrayAsync(cancellationToken), links, cancellationToken));
}
