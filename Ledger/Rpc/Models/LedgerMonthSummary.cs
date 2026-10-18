namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerMonthSummary(
    DateOnly Month, string Currency, int Count, decimal Income, decimal Expense, decimal Balance,
    LedgerCategoryTotal[] ExpenseCategories);
