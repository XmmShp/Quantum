using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerEntryRequest(
    [property: Description("记账记录的非空 GUID。")] Guid Id);
