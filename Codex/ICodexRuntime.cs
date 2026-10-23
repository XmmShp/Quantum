using Quantum.OfficialPlugins.Codex.Application;

namespace Quantum.OfficialPlugins.Codex;

public interface ICodexRuntime : ICodexRuntimeEvents
{
    CodexIntegrationSnapshot Snapshot
    {
        get;
    }

    /// <summary>Attempts one automatic connection per plugin runtime; repeated views share the attempt.</summary>
    Task EnsureAutoConnectedAsync();

    Task ConnectAsync(
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default);

    Task StartNewThreadAsync(
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the model for subsequent turns without replacing the current conversation.</summary>
    Task ChangeModelAsync(string model, CancellationToken cancellationToken = default);

    Task SendAsync(string prompt, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
