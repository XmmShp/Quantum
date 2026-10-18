namespace Quantum.OfficialPlugins.Ledger;

public enum EntryKind { Expense, Income }

/// <summary>Amounts are positive integer fen; currency is always CNY.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry() { }

    public Guid Id { get; private set; }
    public DateOnly Date { get; private set; }
    public EntryKind Kind { get; private set; }
    public long AmountInFen { get; private set; }
    public string Category { get; private set; } = "";
    public string Note { get; private set; } = "";
    public decimal Amount => AmountInFen / 100m;

    public static LedgerEntry Create(Guid id, DateOnly date, EntryKind kind, decimal amount, string category, string note)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invalid record ID.");
        var item = new LedgerEntry { Id = id };
        item.Update(date, kind, amount, category, note);
        return item;
    }

    public void Update(DateOnly date, EntryKind kind, decimal amount, string category, string note)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(note);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid entry type.");
        if (date.Year < 1900 || date.Year > 9998) throw new ArgumentException("Date must be between 1900 and 9998.");
        if (amount <= 0 || amount > 999999999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Amount must be positive, at most 999,999,999.99, with up to two decimal places.");
        category = category.Trim();
        if (category.Length is < 1 or > 40 || category.Any(char.IsControl))
            throw new ArgumentException("Category must contain 1–40 characters and no control characters.");
        if (note.Length > 1000) throw new ArgumentException("Note cannot exceed 1,000 characters.");
        Date = date;
        Kind = kind;
        AmountInFen = checked((long)(amount * 100));
        Category = category;
        Note = note;
    }

    public bool SameContent(LedgerEntry other) => Date == other.Date && Kind == other.Kind &&
        AmountInFen == other.AmountInFen && Category == other.Category && Note == other.Note;
}

public sealed record LedgerTotals(decimal Income, decimal Expense)
{
    public decimal Balance => Income - Expense;
    public static LedgerTotals From(IEnumerable<LedgerEntry> entries)
    {
        decimal income = 0, expense = 0;
        foreach (var item in entries)
        {
            if (item.Kind == EntryKind.Income) income += item.Amount;
            else expense += item.Amount;
        }
        return new(income, expense);
    }
}
