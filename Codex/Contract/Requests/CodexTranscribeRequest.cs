using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexTranscribeRequest
{
    [Description("User-selected PCM WAV bytes, up to 8 MiB. Local filesystem paths are never accepted.")]
    public required MobileAttachment Audio { get; set; }

    [Description("Optional BCP-47 language tag. Must match an installed system speech language.")]
    public string? Language { get; set; }
}
