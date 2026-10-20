namespace Quantum.OfficialPlugins.Ledger.Pages;

internal sealed record CalendarLinkDetails(Guid Id, string Title, DateOnly Date, string Kind, string Notes, TimeOnly StartTime, TimeOnly? EndTime, bool IsCompleted);
