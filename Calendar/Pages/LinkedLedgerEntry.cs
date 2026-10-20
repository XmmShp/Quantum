namespace Quantum.OfficialPlugins.Calendar.Pages;

internal sealed record LinkedLedgerEntry(Guid Id, DateOnly Date, int Kind, decimal Amount, string Category, string Note);
