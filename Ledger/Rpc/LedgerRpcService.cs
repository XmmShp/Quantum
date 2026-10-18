using System.ComponentModel;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerMonthRequest(
    [property: Description("月份中的任意一天，格式 yyyy-MM-dd；按整月查询，1900–9998 年。")] DateOnly Month);

public sealed record LedgerEntryRequest(
    [property: Description("记账记录的非空 GUID。")] Guid Id);

public sealed record SaveLedgerEntryRequest(
    [property: Description("记账日期，格式 yyyy-MM-dd，1900–9998 年。")] DateOnly Date,
    [property: Description("收支类型：0 为支出 Expense，1 为收入 Income。")] EntryKind Kind,
    [property: Description("人民币元金额，必须大于零、不超过 999999999.99，最多两位小数。")] decimal Amount,
    [property: Description("分类名称，1–40 个字符，不允许控制字符。")] string Category,
    [property: Description("备注，最多 1000 个字符。")] string Note = "");

public sealed record UpdateLedgerEntryRequest(
    [property: Description("要更新的记录 GUID。")] Guid Id,
    [property: Description("完整替换记录的新内容，不改变记录 ID。")] SaveLedgerEntryRequest Entry);

public sealed record ImportLedgerCsvRequest(
    [property: Description("CSV 文本，表头 id,date,type,amount,category,note；最多 1000 条记录 / UTF-8 2 MiB。相同 ID 内容一致时跳过，不一致时整次拒绝。")] string Csv);

public sealed record LedgerEntryDetails(
    Guid Id, DateOnly Date, EntryKind Kind, decimal Amount, string Category, string Note)
{
    public static LedgerEntryDetails From(LedgerEntry entry) =>
        new(entry.Id, entry.Date, entry.Kind, entry.Amount, entry.Category, entry.Note);
}

public sealed record LedgerEntryLookup(LedgerEntryDetails? Entry);
public sealed record LedgerCategoryTotal(string Category, decimal Amount);
public sealed record LedgerMonthSummary(
    DateOnly Month, string Currency, int Count, decimal Income, decimal Expense, decimal Balance,
    LedgerCategoryTotal[] ExpenseCategories);
public sealed record LedgerCsvExport(string FileName, string ContentType, string Csv);
public sealed record LedgerImportResult(int AddedCount);

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

    [Description("获取整月收入、支出、结余和支出分类合计；金额单位为人民币元。")]
    [RpcInvocationName("summary")]
    Result<LedgerMonthSummary> GetSummary(LedgerMonthRequest request);

    [Description("创建一笔收入或支出并返回包含新 ID 的已保存记录。")]
    [RpcInvocationName("create")]
    Result<LedgerEntryDetails> CreateEntry(SaveLedgerEntryRequest request);

    [Description("完整更新指定 ID 的收支记录；不存在时返回 ledger_entry_not_found。")]
    [RpcInvocationName("update")]
    Result<LedgerEntryDetails> UpdateEntry(UpdateLedgerEntryRequest request);

    [Description("永久删除指定 ID 的收支记录。调用前应确认用户的删除意图；记录不存在时视为成功。")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(LedgerEntryRequest request);

    [Description("导出指定整月的 CSV 文本和建议文件名，不操作文件系统。CSV 对电子表格公式前缀进行保护。")]
    [RpcInvocationName("export-csv")]
    Result<LedgerCsvExport> ExportCsv(LedgerMonthRequest request);

    [Description("原子导入 CSV，保留原日期和 ID；返回新建记录数。重复导入相同记录不会重复记账。调用前应确认用户的导入意图。")]
    [RpcInvocationName("import-csv")]
    Result<LedgerImportResult> ImportCsv(ImportLedgerCsvRequest request);
}

public partial class LedgerRpcService : RpcServer<ILedgerRpcService>;
