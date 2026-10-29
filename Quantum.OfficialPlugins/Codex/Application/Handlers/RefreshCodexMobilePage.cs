using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class RefreshCodexMobilePage(ICodexMobilePages pages) : CodexRpcService.RefreshMobilePage
{
    public override async Task<Result> HandleAsync(RefreshCodexMobilePageRequest request, Context context, CancellationToken cancellationToken)
    {
        await pages.PublishAsync(cancellationToken);
        return Result.Success();
    }
}
