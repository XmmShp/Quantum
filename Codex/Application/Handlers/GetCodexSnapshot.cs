using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class GetCodexSnapshot(ICodexRuntime codex) : CodexRpcService.GetSnapshot
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSnapshotRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(() => Task.FromResult(codex.Snapshot), cancellationToken);
}
