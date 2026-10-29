using System.ComponentModel;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerMonthRequest
{
    [Description("Any date in the requested month, formatted yyyy-MM-dd, in years 1900 through 9998. Queries cover the entire month.")]
    public required DateOnly Month { get; set; }
}
