using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexSendRequest
{
    [Description("Prompt (may be empty when attachments are present) for the current local Codex thread. Starts model inference and may consume account usage or trigger side effects through Quantum tools.")]
    public required string Prompt { get; set; }

    [Description("User-selected image, file or recorded voice bytes. At most four files, 8 MiB each and 16 MiB total. Voice is transcribed before replying in the current conversation.")]
    public MobileAttachment[] Attachments { get; set; } = [];

    [Description("Optional speech language tag for recorded voice, for example zh-CN or en-US.")]
    public string? SpeechLanguage { get; set; }
}
