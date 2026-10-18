using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class DisconnectCodex(ICodexRuntime codex) : CodexRpcService.Disconnect
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexDisconnectRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            await codex.DisconnectAsync(cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}
