using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record UpdateLedgerEntryRequest(
    [property: Description("要更新的记录 GUID。")] Guid Id,
    [property: Description("完整替换记录的新内容，不改变记录 ID。")] SaveLedgerEntryRequest Entry);
