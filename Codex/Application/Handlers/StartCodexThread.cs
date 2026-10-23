using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class StartCodexThread(ICodexRuntime codex) : CodexRpcService.StartNewThread
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexNewThreadRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await codex.StartNewThreadAsync(request.Model, cancellationToken);
        return codex.Snapshot;
    }
}
