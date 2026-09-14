using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

public enum CodexConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public enum CodexMessageRole
{
    User,
    Assistant,
    System,
    Tool
}

public sealed record CodexChatMessage(
    Guid Id,
    CodexMessageRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    bool IsStreaming = false);

public sealed record PendingQuantumRpcCall(
    string CallId,
    string ToolName,
    string RpcName,
    string Description,
    JsonElement Arguments);

public sealed record CodexIntegrationSnapshot(
    CodexConnectionState ConnectionState,
    string? ThreadId,
    bool IsBusy,
    int ToolCount,
    string WorkingDirectory,
    IReadOnlyList<CodexChatMessage> Messages,
    IReadOnlyList<PendingQuantumRpcCall> PendingCalls,
    string? ErrorMessage);

public interface ICodexIntegrationService
{
    event Action? Changed;

    CodexIntegrationSnapshot Snapshot { get; }

    Task ConnectAsync(string workingDirectory, CancellationToken cancellationToken = default);

    Task StartNewThreadAsync(CancellationToken cancellationToken = default);

    Task SendAsync(string prompt, CancellationToken cancellationToken = default);

    Task ResolveToolCallAsync(
        string callId,
        bool approved,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

internal sealed record QuantumRpcTool(
    string ToolName,
    string RpcName,
    string Description,
    bool ReturnsValue,
    JsonElement InputSchema);

internal sealed record CodexDynamicToolCall(
    string CallId,
    string? Namespace,
    string Tool,
    JsonElement Arguments);

internal sealed record CodexDynamicToolResult(bool Success, string Text);
