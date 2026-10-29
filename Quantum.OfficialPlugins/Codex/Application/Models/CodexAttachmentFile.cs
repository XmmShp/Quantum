namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed class CodexAttachmentFile
{
    public required string Name
    {
        get; init;
    }
    public required string ContentType
    {
        get; init;
    }
    public required string Path
    {
        get; init;
    }
    public required long Size
    {
        get; init;
    }
}
