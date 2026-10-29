namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexIntegrationSnapshot
{
    public required CodexConnectionState ConnectionState
    {
        get; set;
    }

    public required string? ThreadId
    {
        get; set;
    }

    public required bool IsBusy
    {
        get; set;
    }

    public required int ToolCount
    {
        get; set;
    }

    public required string WorkingDirectory
    {
        get; set;
    }

    public required IReadOnlyList<CodexModelOption> Models
    {
        get; set;
    }

    public required string SelectedModel
    {
        get; set;
    }

    public required IReadOnlyList<CodexChatMessage> Messages
    {
        get; set;
    }

    public required string? ErrorMessage
    {
        get; set;
    }
}
