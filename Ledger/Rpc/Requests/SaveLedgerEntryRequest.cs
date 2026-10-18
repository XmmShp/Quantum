using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record SaveLedgerEntryRequest(
    [property: Description("记账日期，格式 yyyy-MM-dd，1900–9998 年。")] DateOnly Date,
    [property: Description("收支类型：0 为支出 Expense，1 为收入 Income。")] EntryKind Kind,
    [property: Description("人民币元金额，必须大于零、不超过 999999999.99，最多两位小数。")] decimal Amount,
    [property: Description("分类名称，1–40 个字符，不允许控制字符。")] string Category,
    [property: Description("备注，最多 1000 个字符。")] string Note = "",
    [property: Description("可选：收入所关联的支出 GUID，支持跨月和多笔收入关联同一支出。省略或 null 表示无关联或解除关联。")] Guid? RelatedExpenseId = null);
