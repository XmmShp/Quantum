using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record CalendarLedgerRequest(
    [property: Description("日程或待办的非空 GUID。")] Guid CalendarEntryId);
