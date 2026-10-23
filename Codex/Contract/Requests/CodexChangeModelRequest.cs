using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexChangeModelRequest
{
    [Description("An available model ID for subsequent turns. Retains the current thread ID and messages. Rejected while a turn is running.")]
    public required string Model { get; set; }
}
