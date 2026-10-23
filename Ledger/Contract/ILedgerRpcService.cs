using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

[Description("Manage the local Quantum CNY ledger: entries, monthly totals, and bidirectional links between entries of any kind. Mutations write to the local database.")]
[TransportOverQuantum]
[RpcInvocationName("ledger")]
public interface ILedgerRpcService : IRpcService
{
    [Description("List all income and expense entries for a month in descending date order. Amounts are in CNY yuan.")]
    [RpcInvocationName("list")]
    Result<LedgerEntryDetails[]> ListEntries(LedgerMonthRequest request);

    [Description("Get an entry by GUID; returns entry=null if it does not exist.")]
    [RpcInvocationName("get")]
    Result<LedgerEntryLookup> GetEntry(LedgerEntryRequest request);

    [Description("List bidirectionally linked entries across months and entry kinds. Returns ledger_entry_not_found if the source entry does not exist. Links do not affect financial totals.")]
    [RpcInvocationName("related")]
    Result<LedgerEntryDetails[]> GetRelatedEntries(LedgerEntryRequest request);

    [Description("List entries linked to a calendar event or task GUID across months. Returns an empty array if there are no matches. The calendar plugin and target item need not exist.")]
    [RpcInvocationName("by-calendar-entry")]
    Result<LedgerEntryDetails[]> ListByCalendarEntry(CalendarLedgerRequest request);

    [Description("Get monthly income, expenses, balance, and expense category totals in CNY yuan.")]
    [RpcInvocationName("summary")]
    Result<LedgerMonthSummary> GetSummary(LedgerMonthRequest request);

    [Description("Create an income or expense entry and return the saved entry with its new ID.")]
    [RpcInvocationName("create")]
    Result<LedgerEntryDetails> CreateEntry(SaveLedgerEntryRequest request);

    [Description("Completely replace an entry and its bidirectional links. Omitted or empty relatedEntryIds removes all entry links. The entry kind may change. Returns ledger_entry_not_found if the entry does not exist.")]
    [RpcInvocationName("update")]
    Result<LedgerEntryDetails> UpdateEntry(UpdateLedgerEntryRequest request);

    [Description("Permanently delete an entry and its links without deleting linked entries. Confirm the user intends to delete it before calling. A missing entry is treated as success.")]
    [RpcInvocationName("delete")]
    Result DeleteEntry(LedgerEntryRequest request);
}
