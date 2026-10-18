using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class CreateLedgerEntry(IRepository<LedgerEntry> entries, IDbContext database) : LedgerRpcService.CreateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(SaveLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            var item = LedgerEntry.Create(Guid.NewGuid(), request.Date, request.Kind, request.Amount,
                request.Category, request.Note, request.RelatedExpenseId);
            await LedgerQueries.ValidateRelationAsync(entries, item, null, cancellationToken);
            await entries.AddAsync(item, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return LedgerEntryDetails.From(item);
        });
}
