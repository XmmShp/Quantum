using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed record QuantumRpcTool
{
    public required string ToolName
    {
        get; set;
    }
    public required string RpcName
    {
        get; set;
    }
    public required string Description
    {
        get; set;
    }
    public required bool ReturnsValue
    {
        get; set;
    }
    public required JsonElement InputSchema
    {
        get; set;
    }
}
