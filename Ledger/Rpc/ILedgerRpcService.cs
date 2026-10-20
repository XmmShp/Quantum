using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

[Description("管理 Quantum 本地人民币账本：查询收支、月度统计、创建修改删除记录及任意类型账单的双向线索关联。修改操作会写入本机数据库。")]
[TransportOverQuantum]
[RpcInvocationName("ledger")]
public interface ILedgerRpcService : IRpcService
{
    [Description("列出指定月份的全部收支记录，按日期倒序。金额单位为人民币元。")]
    [RpcInvocationName("list")]
    Result<LedgerEntryDetails[]> ListEntries(LedgerMonthRequest request);

    [Description("按 GUID 查询单笔记录；不存在时返回 entry=null。")]
    [RpcInvocationName("get")]
    Result<LedgerEntryLookup> GetEntry(LedgerEntryRequest request);

    [Description("跨月查询任意类型的双向关联账单；不存在时返回 ledger_entry_not_found。关联仅作为线索，不改变收支统计。")]
    [RpcInvocationName("related")]
    Result<LedgerEntryDetails[]> GetRelatedEntries(LedgerEntryRequest request);

    [Description("跨月列出关联指定日程或待办 GUID 的收支。无关联时返回空数组；不依赖日历插件或目标是否仍存在。")]
    [RpcInvocationName("by-calendar-entry")]
    Result<LedgerEntryDetails[]> ListByCalendarEntry(CalendarLedgerRequest request);

    [Description("获取整月收入、支出、结余和支出分类合计；金额单位为人民币元。")]
    [RpcInvocationName("summary")]
    Result<LedgerMonthSummary> GetSummary(LedgerMonthRequest request);

    [Description("创建一笔收入或支出并返回包含新 ID 的已保存记录。")]
    [RpcInvocationName("create")]
    Result<LedgerEntryDetails> CreateEntry(SaveLedgerEntryRequest request);

    [Description("完整更新指定 ID 的收支记录和双向关联集合；省略或清空 relatedEntryIds 表示解除该账单的全部关联。允许自由修改收入/支出类型；不存在时返回 ledger_entry_not_found。")]
    [RpcInvocationName("update")]
    Result<LedgerEntryDetails> UpdateEntry(UpdateLedgerEntryRequest request);

    [Description("永久删除指定 ID 的收支记录并移除其关联线索，不删除关联账单。调用前应确认用户的删除意图；记录不存在时视为成功。")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(LedgerEntryRequest request);

}
