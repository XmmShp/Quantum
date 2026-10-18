using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerMonthRequest(
    [property: Description("月份中的任意一天，格式 yyyy-MM-dd；按整月查询，1900–9998 年。")] DateOnly Month);
