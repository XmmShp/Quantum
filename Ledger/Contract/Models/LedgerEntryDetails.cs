namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryDetails
{
    public required Guid Id { get; set; }

    public required DateOnly Date { get; set; }

    public required EntryKind Kind { get; set; }

    public required decimal Amount { get; set; }

    public required string Category { get; set; }

    public required string Note { get; set; }

    public required Guid[] RelatedEntryIds { get; set; }

    public Guid[]? CalendarEntryIds { get; set; }
}
