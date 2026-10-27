namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexAttachmentInfo
{
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public required long Size { get; set; }
}
