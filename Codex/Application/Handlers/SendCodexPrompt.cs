using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class SendCodexPrompt(ICodexRuntime codex) : CodexRpcService.Send
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSendRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
            await codex.SendAsync(request.Prompt, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}
