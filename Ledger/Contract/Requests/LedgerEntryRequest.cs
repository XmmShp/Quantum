using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryRequest
{
    [Description("Non-empty ledger entry GUID.")]
    public required Guid Id { get; set; }
}
