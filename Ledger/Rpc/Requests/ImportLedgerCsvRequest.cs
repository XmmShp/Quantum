using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record ImportLedgerCsvRequest(
    [property: Description("CSV 文本，表头 id,date,type,amount,category,note，可追加 related_expense_id 列。关联支出须已存在或在同批中。最多 1000 条 / UTF-8 2 MiB；相同 ID 内容一致时跳过，不一致时整次拒绝。")] string Csv);
