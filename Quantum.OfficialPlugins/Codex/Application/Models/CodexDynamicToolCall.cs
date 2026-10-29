using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed record CodexDynamicToolCall
{
    public required string CallId
    {
        get; set;
    }
    public required string? Namespace
    {
        get; set;
    }
    public required string Tool
    {
        get; set;
    }
    public required JsonElement Arguments
    {
        get; set;
    }
}
