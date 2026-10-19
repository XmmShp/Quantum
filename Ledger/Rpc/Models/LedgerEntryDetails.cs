namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryDetails(
    Guid Id, DateOnly Date, EntryKind Kind, decimal Amount, string Category, string Note,
    Guid[] RelatedEntryIds)
{
    public static LedgerEntryDetails From(LedgerEntry entry, Guid[] relatedEntryIds) =>
        new(entry.Id, entry.Date, entry.Kind, entry.Amount, entry.Category, entry.Note, relatedEntryIds);
}
