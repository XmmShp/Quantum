using Quantum.OfficialPlugins.Codex.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantum.OfficialPlugins.Codex.Infrastructure;

internal interface ICodexAppServerClient : IAsyncDisposable
{
    event Func<string, JsonElement, Task>? NotificationReceived;
    event Action<Exception?>? ConnectionClosed;
    event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>? DynamicToolCallReceived;
    Task StartAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> StartThreadAsync(string workingDirectory, string model, JsonArray dynamicTools, CancellationToken cancellationToken);
    Task<JsonElement> StartTurnAsync(string threadId, string prompt, string model, CancellationToken cancellationToken);
}
