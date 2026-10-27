using Quantum.OfficialPlugins.Codex.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantum.OfficialPlugins.Codex;

internal interface ICodexAppServerClient : IAsyncDisposable
{
    event Func<string, JsonElement, Task>? NotificationReceived;
    event Action<Exception?>? ConnectionClosed;
    event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>? DynamicToolCallReceived;
    Task StartAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> StartThreadAsync(string workingDirectory, string model, JsonArray dynamicTools, CancellationToken cancellationToken);
    Task<JsonElement> StartMediaTurnAsync(string threadId, string prompt, string model, CodexAttachmentFile[] files, CancellationToken cancellationToken)
        => files.Length == 0 ? StartTurnAsync(threadId, prompt, model, cancellationToken) : throw new NotSupportedException("Attachments are unavailable.");
    Task<JsonElement> StartTurnAsync(string threadId, string prompt, string model, CancellationToken cancellationToken);
}
