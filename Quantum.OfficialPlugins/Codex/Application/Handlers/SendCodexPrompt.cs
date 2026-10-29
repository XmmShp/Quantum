using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class SendCodexPrompt(ICodexRuntime codex) : CodexRpcService.Send
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSendRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Attachments is null || string.IsNullOrWhiteSpace(request.Prompt) && request.Attachments.Length == 0)
        {
            throw new DomainException("codex_invalid_request", "Prompt is required.");
        }
        await codex.SendAsync(request, cancellationToken);
        return codex.Snapshot;
    }
}
