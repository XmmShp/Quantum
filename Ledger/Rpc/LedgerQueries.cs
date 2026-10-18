using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal static class LedgerQueries
{
    public static IQueryable<LedgerEntry> Month(IRepository<LedgerEntry> entries, DateOnly month)
    {
        if (month.Year is < 1900 or > 9998)
        {
            throw new ArgumentException("Date must be between 1900 and 9998.");
        }

        var start = new DateOnly(month.Year, month.Month, 1);
        var end = start.AddMonths(1);
        return entries.AsNoTracking().Where(item => item.Date >= start && item.Date < end)
            .OrderByDescending(item => item.Date).ThenBy(item => item.Id);
    }

    public static async Task ValidateRelationAsync(IRepository<LedgerEntry> entries, LedgerEntry item,
        IReadOnlyDictionary<Guid, LedgerEntry>? imported, CancellationToken cancellationToken)
    {
        if (item.RelatedExpenseId is not { } expenseId)
        {
            return;
        }

        var expense = imported is not null && imported.TryGetValue(expenseId, out var found)
            ? found : await entries.AsNoTracking().Where(other => other.Id == expenseId).SingleOrDefaultAsync(cancellationToken);
        if (expense is null)
        {
            throw new KeyNotFoundException("Related expense does not exist.");
        }

        if (expense.Kind != EntryKind.Expense)
        {
            throw new ArgumentException("The linked record must be an expense.");
        }
    }
}
