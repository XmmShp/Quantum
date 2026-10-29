namespace Quantum.OfficialPlugins.Ledger.UI.Pages;

internal sealed record CalendarLinkDetails
{
    public required Guid Id
    {
        get; set;
    }
    public required string Title
    {
        get; set;
    }
    public required DateOnly Date
    {
        get; set;
    }
    public required string Kind
    {
        get; set;
    }
    public required string Notes
    {
        get; set;
    }
    public required TimeOnly StartTime
    {
        get; set;
    }
    public required TimeOnly? EndTime
    {
        get; set;
    }
    public required bool IsCompleted
    {
        get; set;
    }
}
