using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexSendRequest
{
    [Description("Non-empty prompt for the current local Codex thread. Starts model inference and may consume account usage or trigger side effects through Quantum tools.")]
    public required string Prompt { get; set; }
}
