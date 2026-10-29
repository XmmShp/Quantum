using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record UpdateLedgerEntryRequest
{
    [Description("GUID of the entry to update.")]
    public required Guid Id { get; set; }

    [Description("The complete replacement contents; the entry ID is unchanged.")]
    public required SaveLedgerEntryRequest Entry { get; set; }
}
