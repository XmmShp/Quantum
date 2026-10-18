namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed record LedgerCsvExport(string FileName, string ContentType, string Csv);
