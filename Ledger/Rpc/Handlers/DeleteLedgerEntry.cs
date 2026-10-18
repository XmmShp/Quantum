using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class DeleteLedgerEntry(IRepository<LedgerEntry> entries, IDbContext database) : LedgerRpcService.DeleteEntry
{
    public override Task<Result> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken);
            if (item is null)
            {
                return;
            }

            if (await entries.AsNoTracking().Where(other => other.RelatedExpenseId == request.Id).AnyAsync(cancellationToken))
            {
                throw new ArgumentException("Unlink repayments before deleting this expense.");
            }

            entries.Remove(item);
            await database.SaveChangesAsync(cancellationToken);
        });
}
