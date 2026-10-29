using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record SaveLedgerEntryRequest
{
    [Description("Entry date formatted yyyy-MM-dd, in years 1900 through 9998.")]
    public required DateOnly Date { get; set; }

    [Description("Entry kind: Expense or Income, serialized as a JSON string.")]
    public required EntryKind Kind { get; set; }

    [Description("Amount in CNY yuan, greater than zero and at most 999999999.99, with up to two decimal places.")]
    public required decimal Amount { get; set; }

    [Description("Category name, 1 to 40 characters without control characters.")]
    public required string Category { get; set; }

    [Description("Note, up to 1,000 characters.")]
    public string Note { get; set; } = string.Empty;

    [Description("Optional GUIDs of related entries, supporting any kind, multiple entries, cross-month and bidirectional links. Must not include this entry or empty GUIDs. Omitted or empty values remove all entry links on full update.")]
    public Guid[]? RelatedEntryIds { get; set; }

    [Description("Linked calendar event or task GUIDs, at most 100. Defaults to empty on creation. On update, omitted or null preserves links; an empty array removes all links. Offline references are supported; the calendar plugin need not be installed.")]
    public Guid[]? CalendarEntryIds { get; set; }
}
