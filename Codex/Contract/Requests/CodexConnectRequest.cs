using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexConnectRequest
{
    [Description("An existing local working directory; an empty string uses the current Quantum working directory. Connecting starts the local Codex App Server and exposes Quantum tools from other plugins.")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [Description("Model ID supported by the local Codex installation. Defaults to gpt-5.6-luna.")]
    public string Model { get; set; } = "gpt-5.6-luna";
}
