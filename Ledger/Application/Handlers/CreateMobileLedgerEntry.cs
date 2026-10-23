using NOF.Contract;
using NOF.Domain;
using System.Globalization;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class CreateMobileLedgerEntry(ILedgerRpcServiceClient client, TimeProvider? timeProvider = null) : LedgerRpcService.CreateMobileEntry
{
    public override Task<Result<LedgerEntryDetails>> HandleAsync(CreateMobileLedgerEntryRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!decimal.TryParse(request.AmountText?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount))
        {
            throw new DomainException("ledger_invalid_amount", "Enter an amount using a dot decimal separator without grouping or currency symbols.");
        }
        return client.CreateEntryAsync(new SaveLedgerEntryRequest
        {
            Date = DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetLocalNow().DateTime),
            Kind = request.IsIncome ? EntryKind.Income : EntryKind.Expense,
            Amount = amount,
            Category = request.Category,
            Note = request.Note
        }, context, cancellationToken);
    }
}
