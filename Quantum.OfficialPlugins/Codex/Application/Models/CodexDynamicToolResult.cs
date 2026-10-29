namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed record CodexDynamicToolResult
{
    public required bool Success
    {
        get; set;
    }
    public required string Text
    {
        get; set;
    }
}
