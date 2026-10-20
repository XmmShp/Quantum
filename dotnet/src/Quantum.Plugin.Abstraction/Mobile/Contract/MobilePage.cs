namespace Quantum.Plugin.Abstraction;

public sealed record MobilePage
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required MobileNode Root { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>Executed only in the desktop plugin, never interpreted by the mobile client.</summary>
