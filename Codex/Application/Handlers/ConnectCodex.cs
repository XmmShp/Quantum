using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ConnectCodex(ICodexRuntime codex) : CodexRpcService.Connect
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexConnectRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request.WorkingDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
            await codex.ConnectAsync(request.WorkingDirectory, request.Model, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = codex.Snapshot;
            if (snapshot.ConnectionState != CodexConnectionState.Connected)
            {
                throw new CodexConnectionException(snapshot.ErrorMessage ?? "Codex connection did not complete.");
            }

            return snapshot;
        }, cancellationToken);
}
