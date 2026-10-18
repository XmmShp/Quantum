using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

[Description("管理 Quantum 本地人民币账本：查询收支、月度统计、创建修改删除记录及 CSV 导入导出。修改操作会写入本机数据库。")]
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

    [Description("跨月查询关联明细：收入返回原支出，支出返回所有报销和 AA 回款；不存在时返回 ledger_entry_not_found。原始收支统计不因关联改变。")]
    [RpcInvocationName("related")]
    Result<LedgerEntryDetails[]> GetRelatedEntries(LedgerEntryRequest request);

    [Description("获取整月收入、支出、结余和支出分类合计；金额单位为人民币元。")]
    [RpcInvocationName("summary")]
    Result<LedgerMonthSummary> GetSummary(LedgerMonthRequest request);

    [Description("创建一笔收入或支出并返回包含新 ID 的已保存记录。")]
    [RpcInvocationName("create")]
    Result<LedgerEntryDetails> CreateEntry(SaveLedgerEntryRequest request);

    [Description("完整更新指定 ID 的收支记录；未提供关联字段表示解除关联。被回款关联的支出不能改为收入；不存在时返回 ledger_entry_not_found。")]
    [RpcInvocationName("update")]
    Result<LedgerEntryDetails> UpdateEntry(UpdateLedgerEntryRequest request);

    [Description("永久删除指定 ID 的收支记录。存在回款关联的支出须先解除关联。调用前应确认用户的删除意图；记录不存在时视为成功。")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(LedgerEntryRequest request);

    [Description("导出指定整月的 CSV 文本和建议文件名，不操作文件系统。CSV 对电子表格公式前缀进行保护。")]
    [RpcInvocationName("export-csv")]
    Result<LedgerCsvExport> ExportCsv(LedgerMonthRequest request);

    [Description("原子导入 CSV，保留原日期和 ID；返回新建记录数。重复导入相同记录不会重复记账。调用前应确认用户的导入意图。")]
    [RpcInvocationName("import-csv")]
    Result<LedgerImportResult> ImportCsv(ImportLedgerCsvRequest request);
}
