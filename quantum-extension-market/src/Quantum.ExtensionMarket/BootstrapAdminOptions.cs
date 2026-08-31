namespace Quantum.ExtensionMarket;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "ExtensionMarket:BootstrapAdmin";

    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
