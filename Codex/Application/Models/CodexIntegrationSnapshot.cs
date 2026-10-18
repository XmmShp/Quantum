namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexIntegrationSnapshot(
    CodexConnectionState ConnectionState,
    string? ThreadId,
    bool IsBusy,
    int ToolCount,
    string WorkingDirectory,
    IReadOnlyList<CodexModelOption> Models,
    string SelectedModel,
    IReadOnlyList<CodexChatMessage> Messages,
    string? ErrorMessage);
