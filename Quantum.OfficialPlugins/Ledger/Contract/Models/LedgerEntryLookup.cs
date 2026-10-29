namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryLookup
{
    public required LedgerEntryDetails? Entry { get; set; }
}
