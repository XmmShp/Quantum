using System.ComponentModel.DataAnnotations;

namespace Quantum.Platform;

public sealed class PlatformEmailOptions
{
    public const string SectionName = "QuantumPlatform:Email";

    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = string.Empty;

    [Range(1, 65_535)]
    public int SmtpPort { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Quantum Platform";

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
