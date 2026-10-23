using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexNewThreadRequest
{
    [Description("Optional model ID; omitted values retain the current model. Clears the currently displayed thread messages.")]
    public string? Model { get; set; }
}
