using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record CalendarLedgerRequest
{
    [Description("Non-empty GUID of a calendar event or task.")]
    public required Guid CalendarEntryId { get; set; }
}
