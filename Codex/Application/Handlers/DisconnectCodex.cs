using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class DisconnectCodex(ICodexRuntime codex) : CodexRpcService.Disconnect
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexDisconnectRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await codex.DisconnectAsync(cancellationToken);
        return codex.Snapshot;
    }
}
