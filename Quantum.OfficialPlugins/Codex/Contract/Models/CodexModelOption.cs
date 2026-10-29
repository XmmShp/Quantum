namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexModelOption
{
    public required string Id { get; set; }

    public required string DisplayName { get; set; }

    public string[] InputModalities { get; set; } = ["text", "image"];
}
