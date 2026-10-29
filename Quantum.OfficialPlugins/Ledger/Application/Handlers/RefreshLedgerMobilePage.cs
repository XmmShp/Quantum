using NOF.Contract;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

public sealed class RefreshLedgerMobilePage(ILedgerMobilePages pages) : LedgerRpcService.RefreshMobilePage
{
    public override async Task<Result> HandleAsync(RefreshLedgerMobilePageRequest request, Context context, CancellationToken cancellationToken)
    {
        await pages.PublishAsync(cancellationToken);
        return Result.Success();
    }
}
