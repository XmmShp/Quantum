namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerMonthSummary
{
    public required DateOnly Month { get; set; }

    public required string Currency { get; set; }

    public required int Count { get; set; }

    public required decimal Income { get; set; }

    public required decimal Expense { get; set; }

    public required decimal Balance { get; set; }

    public required LedgerCategoryTotal[] ExpenseCategories { get; set; }
}
