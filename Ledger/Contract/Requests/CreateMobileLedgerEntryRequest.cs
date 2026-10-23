using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record CreateMobileLedgerEntryRequest
{
    [Description("Amount in CNY yuan as plain decimal text with a dot separator, without grouping or currency symbols.")]
    public required string AmountText { get; set; }
    [Description("Category name, 1 to 40 characters.")]
    public required string Category { get; set; }
    [Description("Optional note, up to 1,000 characters.")]
    public string Note { get; set; } = string.Empty;
    [Description("True for income; false for an expense. The date is today in the desktop's local time zone.")]
    public bool IsIncome { get; set; }
}
