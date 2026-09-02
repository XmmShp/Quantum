namespace Quantum.Platform;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "QuantumPlatform:BootstrapAdmin";

    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
