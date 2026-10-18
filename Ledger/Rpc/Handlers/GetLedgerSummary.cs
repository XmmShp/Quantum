using NOF.Application;
using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class GetLedgerSummary(IRepository<LedgerEntry> entries) : LedgerRpcService.GetSummary
{
    public override Task<Result<LedgerMonthSummary>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            var monthEntries = await LedgerQueries.Month(entries, request.Month).ToArrayAsync(cancellationToken);
            var totals = LedgerTotals.From(monthEntries);
            return new LedgerMonthSummary(new(request.Month.Year, request.Month.Month, 1), "CNY", monthEntries.Length,
                totals.Income, totals.Expense, totals.Balance,
                monthEntries.Where(item => item.Kind == EntryKind.Expense).GroupBy(item => item.Category)
                    .Select(group => new LedgerCategoryTotal(group.Key, group.Sum(item => item.Amount)))
                    .OrderByDescending(item => item.Amount).ThenBy(item => item.Category, StringComparer.Ordinal).ToArray());
        });
}
