using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class GetCodexSnapshot(ICodexRuntime codex) : CodexRpcService.GetSnapshot
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSnapshotRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<CodexIntegrationSnapshot>>(codex.Snapshot);
    }
}
