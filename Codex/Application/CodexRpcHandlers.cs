using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class GetCodexSnapshot(ICodexIntegrationService codex) : CodexRpcService.GetSnapshot
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSnapshotRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(() => Task.FromResult(codex.Snapshot), cancellationToken);
}

public sealed class ListCodexModels(ICodexIntegrationService codex) : CodexRpcService.ListModels
{
    public override Task<Result<CodexModelOption[]>> HandleAsync(CodexModelsRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(() => Task.FromResult(codex.Snapshot.Models.ToArray()), cancellationToken);
}

public sealed class ConnectCodex(ICodexIntegrationService codex) : CodexRpcService.Connect
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
                throw new CodexConnectionException(snapshot.ErrorMessage ?? "Codex connection did not complete.");
            return snapshot;
        }, cancellationToken);
}

public sealed class StartCodexThread(ICodexIntegrationService codex) : CodexRpcService.StartNewThread
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexNewThreadRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            await codex.StartNewThreadAsync(request.Model, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}

public sealed class ChangeCodexModel(ICodexIntegrationService codex) : CodexRpcService.ChangeModel
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexChangeModelRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
            await codex.ChangeModelAsync(request.Model, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}

public sealed class SendCodexPrompt(ICodexIntegrationService codex) : CodexRpcService.Send
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexSendRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
            await codex.SendAsync(request.Prompt, cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}

public sealed class DisconnectCodex(ICodexIntegrationService codex) : CodexRpcService.Disconnect
{
    public override Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexDisconnectRequest request, Context context, CancellationToken cancellationToken)
        => CodexRpcExecution.RunAsync(async () =>
        {
            await codex.DisconnectAsync(cancellationToken);
            return codex.Snapshot;
        }, cancellationToken);
}

internal sealed class CodexConnectionException(string message) : Exception(message);

internal static class CodexRpcExecution
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        catch (CodexConnectionException ex) { return Result.Fail("codex_connection_failed", ex.Message); }
        catch (ArgumentException ex) { return Result.Fail("codex_invalid_request", ex.Message); }
        catch (DirectoryNotFoundException ex) { return Result.Fail("codex_invalid_request", ex.Message); }
        catch (InvalidOperationException ex) { return Result.Fail("codex_invalid_state", ex.Message); }
    }
}
