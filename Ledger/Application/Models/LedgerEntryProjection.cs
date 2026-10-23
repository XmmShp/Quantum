namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal sealed record LedgerEntryProjection
{
    public required LedgerEntryDetails Entry
    {
        get; set;
    }

    public required string CalendarEntryIdsJson
    {
        get; set;
    }
}
