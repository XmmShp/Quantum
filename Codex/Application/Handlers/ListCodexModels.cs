using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ListCodexModels(ICodexRuntime codex) : CodexRpcService.ListModels
{
    public override Task<Result<CodexModelOption[]>> HandleAsync(CodexModelsRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<CodexModelOption[]>>(codex.Snapshot.Models.ToArray());
    }
}
