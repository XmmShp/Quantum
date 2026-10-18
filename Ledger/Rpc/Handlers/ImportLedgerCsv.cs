using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using System.Text;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ImportLedgerCsv(IRepository<LedgerEntry> entries, IDbContext database) : LedgerRpcService.ImportCsv
{
    public override Task<Result<LedgerImportResult>> HandleAsync(ImportLedgerCsvRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            ArgumentNullException.ThrowIfNull(request.Csv);
            if (Encoding.UTF8.GetByteCount(request.Csv) > 2 * 1024 * 1024)
            {
                throw new ArgumentException("CSV exceeds 2 MiB.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var imported = LedgerCsv.Parse(request.Csv);
            if (imported.Count is < 1 or > LedgerCsv.MaxRecords || imported.Select(item => item.Id).Distinct().Count() != imported.Count)
            {
                throw new ArgumentException("CSV must contain 1–1,000 records with unique IDs.");
            }
            // One database commit: a bad file cannot produce a partial import.
            var ids = imported.Select(item => item.Id).ToArray();
            var existing = await entries.AsNoTracking().Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
            var byId = existing.ToDictionary(item => item.Id);
            foreach (var item in imported)
            {
                if (byId.TryGetValue(item.Id, out var previous) && !previous.SameContent(item))
                {
                    throw new ArgumentException("An imported ID conflicts with an existing record.");
                }
            }

            var importedById = imported.ToDictionary(item => item.Id);
            foreach (var item in imported)
            {
                await LedgerQueries.ValidateRelationAsync(entries, item, importedById, cancellationToken);
            }

            var added = 0;
            foreach (var item in imported)
            {
                if (byId.ContainsKey(item.Id))
                {
                    continue;
                }

                await entries.AddAsync(item, cancellationToken);
                added++;
            }
            if (added > 0)
            {
                await database.SaveChangesAsync(cancellationToken);
            }

            return new LedgerImportResult(added);
        });
}
