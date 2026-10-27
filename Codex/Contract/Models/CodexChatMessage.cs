namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexChatMessage
{
    public required Guid Id { get; set; }

    public required CodexMessageRole Role { get; set; }

    public required string Text { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public CodexAttachmentInfo[] Attachments { get; set; } = [];

    public bool IsTranscribing { get; set; }

    public bool IsStreaming { get; set; } = false;
}
