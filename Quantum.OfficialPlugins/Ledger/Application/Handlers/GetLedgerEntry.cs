using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetLedgerEntry(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links) : LedgerRpcService.GetEntry
{
    public override async Task<Result<LedgerEntryLookup>> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LedgerQueries.ValidateId(request.Id);
        var matching = await LedgerQueries.DetailsAsync(entries.AsNoTracking().Where(item => item.Id == request.Id), links, cancellationToken);
        return new LedgerEntryLookup
        {
            Entry = matching.SingleOrDefault()
        };
    }
}
