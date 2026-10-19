using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetRelatedLedgerEntries(IRepository<LedgerEntry> entries, IRepository<LedgerEntryLink> links) : LedgerRpcService.GetRelatedEntries
{
    public override Task<Result<LedgerEntryDetails[]>> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            _ = await entries.AsNoTracking().Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Record no longer exists. Refresh and try again.");
            var related = await links.AsNoTracking().Where(link => link.LeftEntryId == request.Id || link.RightEntryId == request.Id)
                .ToArrayAsync(cancellationToken);
            var ids = related.Select(link => link.LeftEntryId == request.Id ? link.RightEntryId : link.LeftEntryId).ToArray();
            return await LedgerQueries.DetailsAsync(await entries.AsNoTracking().Where(item => ids.Contains(item.Id))
                .OrderByDescending(item => item.Date).ThenBy(item => item.Id).ToArrayAsync(cancellationToken), links, cancellationToken);
        });
}
