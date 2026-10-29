using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetLedgerSummary(IRepository<LedgerEntry> entries) : LedgerRpcService.GetSummary
{
    public override async Task<Result<LedgerMonthSummary>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monthEntries = await LedgerQueries.Month(entries, request.Month).ToArrayAsync(cancellationToken);
        var totals = LedgerTotals.From(monthEntries);
        return new LedgerMonthSummary
        {
            Month = new(request.Month.Year, request.Month.Month, 1),
            Currency = "CNY",
            Count = monthEntries.Length,
            Income = totals.Income,
            Expense = totals.Expense,
            Balance = totals.Balance,
            ExpenseCategories = monthEntries.Where(item => item.Kind == EntryKind.Expense).GroupBy(item => item.Category).Select(group => new LedgerCategoryTotal { Category = group.Key, Amount = group.Sum(item => item.Amount) }).OrderByDescending(item => item.Amount).ThenBy(item => item.Category, StringComparer.Ordinal).ToArray()
        };
    }
}
