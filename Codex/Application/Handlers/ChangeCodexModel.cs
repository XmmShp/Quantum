using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ChangeCodexModel(ICodexRuntime codex) : CodexRpcService.ChangeModel
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexChangeModelRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
            await codex.ChangeModelAsync(request.Model, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}
