namespace Quantum.OfficialPlugins.Ledger.UI.Pages;

internal sealed record CalendarLinkLookup
{
    public required CalendarLinkDetails? Entry
    {
        get; set;
    }
}
