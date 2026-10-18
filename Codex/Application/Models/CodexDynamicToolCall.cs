using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed record CodexDynamicToolCall(
    string CallId,
    string? Namespace,
    string Tool,
    JsonElement Arguments);
