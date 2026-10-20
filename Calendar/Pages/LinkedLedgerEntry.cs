namespace Quantum.OfficialPlugins.Calendar.Pages;

internal sealed record LinkedLedgerEntry(Guid Id, DateOnly Date, LinkedLedgerKind Kind, decimal Amount, string Category, string Note);
