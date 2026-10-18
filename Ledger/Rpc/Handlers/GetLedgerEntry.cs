using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetLedgerEntry(IRepository<LedgerEntry> entries) : LedgerRpcService.GetEntry
{
    public override Task<Result<LedgerEntryLookup>> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            var entry = await entries.AsNoTracking().Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken);
            return new LedgerEntryLookup(entry is null ? null : LedgerEntryDetails.From(entry));
        });
}
