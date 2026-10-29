using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class EnsureCodexAutoConnected(ICodexRuntime codex) : CodexRpcService.EnsureAutoConnected
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexAutoConnectRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await codex.EnsureAutoConnectedAsync().WaitAsync(cancellationToken);
        return codex.Snapshot;
    }
}
