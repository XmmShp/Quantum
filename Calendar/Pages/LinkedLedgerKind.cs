using System.Text.Json.Serialization;

namespace Quantum.OfficialPlugins.Calendar.Pages;

// Quantum 0.1.8 uses names; accept numeric values from earlier providers too.
[JsonConverter(typeof(JsonStringEnumConverter<LinkedLedgerKind>))]
internal enum LinkedLedgerKind
{
    Expense,
    Income
}
