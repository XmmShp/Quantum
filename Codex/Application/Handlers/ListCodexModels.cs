using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ListCodexModels(ICodexRuntime codex) : CodexRpcService.ListModels
{
    public override Task<Result<CodexModelOption[]>> HandleAsync(CodexModelsRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(() => Task.FromResult(codex.Snapshot.Models.ToArray()), cancellationToken);
}
