using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class UpdateLedgerEntry(IRepository<LedgerEntry> entries, IDbContext database) : LedgerRpcService.UpdateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(UpdateLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            ArgumentNullException.ThrowIfNull(request.Entry);
            var replacement = request.Entry;
            var validated = LedgerEntry.Create(request.Id, replacement.Date, replacement.Kind, replacement.Amount,
                replacement.Category, replacement.Note, replacement.RelatedExpenseId);
            await LedgerQueries.ValidateRelationAsync(entries, validated, null, cancellationToken);
            var item = await entries.Where(item => item.Id == request.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Record no longer exists. Refresh and try again.");
            if (replacement.Kind != EntryKind.Expense && await entries.AsNoTracking()
                .Where(other => other.RelatedExpenseId == request.Id).AnyAsync(cancellationToken))
            {
                throw new ArgumentException("Unlink repayments before changing this expense to income.");
            }

            item.Update(replacement.Date, replacement.Kind, replacement.Amount, replacement.Category,
                replacement.Note, replacement.RelatedExpenseId);
            await database.SaveChangesAsync(cancellationToken);
            return LedgerEntryDetails.From(item);
        });
}
