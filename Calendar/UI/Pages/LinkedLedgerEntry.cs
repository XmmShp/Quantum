namespace Quantum.OfficialPlugins.Calendar.UI.Pages;

internal sealed record LinkedLedgerEntry
{
    public required Guid Id
    {
        get; set;
    }
    public required DateOnly Date
    {
        get; set;
    }
    public required LinkedLedgerKind Kind
    {
        get; set;
    }
    public required decimal Amount
    {
        get; set;
    }
    public required string Category
    {
        get; set;
    }
    public required string Note
    {
        get; set;
    }
}
