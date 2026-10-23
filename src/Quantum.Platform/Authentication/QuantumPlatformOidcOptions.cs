namespace Quantum.Platform.Authentication;

public sealed class QuantumPlatformOidcOptions
{
    public const string SectionName = "QuantumPlatform:OidcServer";

    public string Issuer { get; set; } = "http://localhost:8080/oauth2";
    public string PathBase { get; set; } = "/oauth2";
    public string Audience { get; set; } = "quantum-platform";
    public string SigningKeyEncryptionKey { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;

    public void Validate(bool isDevelopment)
    {
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme is not ("http" or "https") || !PathBase.StartsWith('/') ||
            !string.IsNullOrEmpty(issuer.UserInfo) || !string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment))
        {
            throw new InvalidOperationException("QuantumPlatform:OidcServer Issuer and PathBase are invalid.");
        }

        if (!isDevelopment && (issuer.Scheme != "https" || issuer.IsLoopback || !RequireHttpsMetadata))
        {
            throw new InvalidOperationException("A public HTTPS QuantumPlatform:OidcServer:Issuer and HTTPS metadata are required outside Development.");
        }
    }
}
