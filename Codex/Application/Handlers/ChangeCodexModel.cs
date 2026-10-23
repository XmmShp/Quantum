using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ChangeCodexModel(ICodexRuntime codex) : CodexRpcService.ChangeModel
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexChangeModelRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new DomainException("codex_invalid_request", "Model is required.");
        }
        await codex.ChangeModelAsync(request.Model, cancellationToken);
        return codex.Snapshot;
    }
}
