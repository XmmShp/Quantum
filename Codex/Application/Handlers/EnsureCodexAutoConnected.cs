using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class EnsureCodexAutoConnected(ICodexRuntime codex) : CodexRpcService.EnsureAutoConnected
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexAutoConnectRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            await codex.EnsureAutoConnectedAsync().WaitAsync(cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}
