using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record SaveLedgerEntryRequest(
    [property: Description("记账日期，格式 yyyy-MM-dd，1900–9998 年。")] DateOnly Date,
    [property: Description("收支类型：0 为支出 Expense，1 为收入 Income。")] EntryKind Kind,
    [property: Description("人民币元金额，必须大于零、不超过 999999999.99，最多两位小数。")] decimal Amount,
    [property: Description("分类名称，1–40 个字符，不允许控制字符。")] string Category,
    [property: Description("备注，最多 1000 个字符。")] string Note = "",
    [property: Description("可选：关联账单 GUID 集合，任意收支类型、跨月、多笔和双向关联；不能包含自身或空 GUID。完整更新时省略或空集合会解除该账单全部关联。")] Guid[]? RelatedEntryIds = null);
