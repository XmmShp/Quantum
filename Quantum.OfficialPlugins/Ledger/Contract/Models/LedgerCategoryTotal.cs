namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerCategoryTotal
{
    public required string Category { get; set; }

    public required decimal Amount { get; set; }
}
