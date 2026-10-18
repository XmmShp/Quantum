using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed record QuantumRpcTool(
    string ToolName,
    string RpcName,
    string Description,
    bool ReturnsValue,
    JsonElement InputSchema);
