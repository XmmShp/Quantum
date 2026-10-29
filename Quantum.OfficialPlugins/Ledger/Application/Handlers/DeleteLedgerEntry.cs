using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class DeleteLedgerEntry(IRepository<LedgerEntry> entries, IDbContext database) : LedgerRpcService.DeleteEntry
{
    public override async Task<Result> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LedgerQueries.ValidateId(request.Id);
        var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return Result.Success();
        }

        // The database cascades clue rows while preserving every linked entry.
        entries.Remove(item);
        await database.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
