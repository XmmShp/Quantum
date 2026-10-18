using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class StartCodexThread(ICodexRuntime codex) : CodexRpcService.StartNewThread
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexNewThreadRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            await codex.StartNewThreadAsync(request.Model, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}
