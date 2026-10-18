using System.Globalization;
using System.Text;
using NOF.Contract;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class ListLedgerEntries(LedgerService ledger) : LedgerRpcService.ListEntries
{
    public override Task<Result<LedgerEntryDetails[]>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
            (await ledger.ListMonthAsync(request.Month, cancellationToken)).Select(LedgerEntryDetails.From).ToArray());
}

public sealed class GetLedgerEntry(LedgerService ledger) : LedgerRpcService.GetEntry
{
    public override Task<Result<LedgerEntryLookup>> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            var entry = await ledger.GetAsync(request.Id, cancellationToken);
            return new LedgerEntryLookup(entry is null ? null : LedgerEntryDetails.From(entry));
        });
}

public sealed class GetLedgerSummary(LedgerService ledger) : LedgerRpcService.GetSummary
{
    public override Task<Result<LedgerMonthSummary>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            var entries = await ledger.ListMonthAsync(request.Month, cancellationToken);
            var totals = LedgerTotals.From(entries);
            return new LedgerMonthSummary(new(request.Month.Year, request.Month.Month, 1), "CNY", entries.Length,
                totals.Income, totals.Expense, totals.Balance,
                entries.Where(item => item.Kind == EntryKind.Expense).GroupBy(item => item.Category)
                    .Select(group => new LedgerCategoryTotal(group.Key, group.Sum(item => item.Amount)))
                    .OrderByDescending(item => item.Amount).ThenBy(item => item.Category, StringComparer.Ordinal).ToArray());
        });
}

public sealed class CreateLedgerEntry(LedgerService ledger) : LedgerRpcService.CreateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(SaveLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () => LedgerEntryDetails.From(await ledger.SaveAsync(
            null, request.Date, request.Kind, request.Amount, request.Category, request.Note, cancellationToken)));
}

public sealed class UpdateLedgerEntry(LedgerService ledger) : LedgerRpcService.UpdateEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(UpdateLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            ArgumentNullException.ThrowIfNull(request.Entry);
            return LedgerEntryDetails.From(await ledger.SaveAsync(request.Id, request.Entry.Date, request.Entry.Kind,
                request.Entry.Amount, request.Entry.Category, request.Entry.Note, cancellationToken));
        });
}

public sealed class DeleteLedgerEntry(LedgerService ledger) : LedgerRpcService.DeleteEntry
{
    public override Task<Result> HandleAsync(LedgerEntryRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            LedgerRpcExecution.ValidateId(request.Id);
            await ledger.DeleteAsync(request.Id, cancellationToken);
        });
}

public sealed class ExportLedgerCsv(LedgerService ledger) : LedgerRpcService.ExportCsv
{
    public override Task<Result<LedgerCsvExport>> HandleAsync(LedgerMonthRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () => new LedgerCsvExport(
            "quantum-ledger-" + request.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv", "text/csv;charset=utf-8",
            LedgerCsv.Export(await ledger.ListMonthAsync(request.Month, cancellationToken))));
}

public sealed class ImportLedgerCsv(LedgerService ledger) : LedgerRpcService.ImportCsv
{
    public override Task<Result<LedgerImportResult>> HandleAsync(ImportLedgerCsvRequest request, Context context, CancellationToken cancellationToken)
        => LedgerRpcExecution.RunAsync(request, async () =>
        {
            ArgumentNullException.ThrowIfNull(request.Csv);
            if (Encoding.UTF8.GetByteCount(request.Csv) > 2 * 1024 * 1024) throw new ArgumentException("CSV exceeds 2 MiB.");
            cancellationToken.ThrowIfCancellationRequested();
            return new LedgerImportResult(await ledger.ImportAsync(LedgerCsv.Parse(request.Csv), cancellationToken));
        });
}

internal static class LedgerRpcExecution
{
    public static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invalid record ID.");
    }

    public static async Task<Result<T>> RunAsync<T>(object? request, Func<Task<T>> action)
    {
        try { ArgumentNullException.ThrowIfNull(request); return await action(); }
        catch (KeyNotFoundException ex) { return Result.Fail("ledger_entry_not_found", ex.Message); }
        catch (ArgumentException ex) { return Result.Fail("ledger_invalid_request", ex.Message); }
    }

    public static async Task<Result> RunAsync(object? request, Func<Task> action)
    {
        try { ArgumentNullException.ThrowIfNull(request); await action(); return Result.Success(); }
        catch (KeyNotFoundException ex) { return Result.Fail("ledger_entry_not_found", ex.Message); }
        catch (ArgumentException ex) { return Result.Fail("ledger_invalid_request", ex.Message); }
    }
}
