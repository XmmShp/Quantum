namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryDetails(
    Guid Id, DateOnly Date, EntryKind Kind, decimal Amount, string Category, string Note,
    Guid? RelatedExpenseId)
{
    public static LedgerEntryDetails From(LedgerEntry entry) =>
        new(entry.Id, entry.Date, entry.Kind, entry.Amount, entry.Category, entry.Note, entry.RelatedExpenseId);
}
