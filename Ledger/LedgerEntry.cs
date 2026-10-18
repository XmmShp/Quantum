namespace Quantum.OfficialPlugins.Ledger;

/// <summary>Amounts are positive integer fen; currency is always CNY.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
    }

    public Guid Id
    {
        get; private set;
    }
    public DateOnly Date
    {
        get; private set;
    }
    public EntryKind Kind
    {
        get; private set;
    }
    public long AmountInFen
    {
        get; private set;
    }
    public string Category { get; private set; } = "";
    public string Note { get; private set; } = "";
    public Guid? RelatedExpenseId
    {
        get; private set;
    }
    public decimal Amount => AmountInFen / 100m;

    public static LedgerEntry Create(Guid id, DateOnly date, EntryKind kind, decimal amount, string category, string note,
        Guid? relatedExpenseId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invalid record ID.");
        }

        var item = new LedgerEntry { Id = id };
        item.Update(date, kind, amount, category, note, relatedExpenseId);
        return item;
    }

    public void Update(DateOnly date, EntryKind kind, decimal amount, string category, string note,
        Guid? relatedExpenseId = null)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(note);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Invalid entry type.");
        }

        if (date.Year < 1900 || date.Year > 9998)
        {
            throw new ArgumentException("Date must be between 1900 and 9998.");
        }

        if (amount <= 0 || amount > 999999999.99m || decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("Amount must be positive, at most 999,999,999.99, with up to two decimal places.");
        }

        category = category.Trim();
        if (category.Length is < 1 or > 40 || category.Any(char.IsControl))
        {
            throw new ArgumentException("Category must contain 1–40 characters and no control characters.");
        }

        if (note.Length > 1000)
        {
            throw new ArgumentException("Note cannot exceed 1,000 characters.");
        }

        if (relatedExpenseId == Guid.Empty || relatedExpenseId == Id ||
            relatedExpenseId.HasValue && kind != EntryKind.Income)
        {
            throw new ArgumentException("Only an income can link to another expense.");
        }

        Date = date;
        Kind = kind;
        AmountInFen = checked((long)(amount * 100));
        Category = category;
        Note = note;
        RelatedExpenseId = relatedExpenseId;
    }

    public bool SameContent(LedgerEntry other) => Date == other.Date && Kind == other.Kind &&
        AmountInFen == other.AmountInFen && Category == other.Category && Note == other.Note &&
        RelatedExpenseId == other.RelatedExpenseId;
}
