namespace Quantum.OfficialPlugins.Ledger;

public sealed record LedgerTotals
{
    public required decimal Income
    {
        get; set;
    }
    public required decimal Expense
    {
        get; set;
    }
    public decimal Balance => Income - Expense;

    public static LedgerTotals From(IEnumerable<LedgerEntry> entries)
    {
        decimal income = 0, expense = 0;
        foreach (var item in entries)
        {
            if (item.Kind == EntryKind.Income)
            {
                income += item.Amount;
            }
            else
            {
                expense += item.Amount;
            }
        }

        return new()
        {
            Income = income,
            Expense = expense
        };
    }
}
