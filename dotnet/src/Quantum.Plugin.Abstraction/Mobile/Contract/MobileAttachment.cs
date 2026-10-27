namespace Quantum.Plugin.Abstraction;

/// <summary>User-selected attachment bytes. At most four files, 8 MiB per file and 16 MiB per action.</summary>
public sealed record MobileAttachment
{
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public required string DataBase64 { get; set; }
}
