using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using System.Globalization;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ExportLedgerCsv(IRepository<LedgerEntry> entries) : LedgerRpcService.ExportCsv
{
    public override Task<Result<LedgerCsvExport>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () => new LedgerCsvExport(
            "quantum-ledger-" + request.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv", "text/csv;charset=utf-8",
            LedgerCsv.Export(await LedgerQueries.Month(entries, request.Month).ToArrayAsync(cancellationToken))));
}
