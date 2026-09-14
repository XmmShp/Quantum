namespace Quantum.Marketplace;

public sealed record QuantumMarketplaceOptions(Uri BaseAddress, string QuantumVersion)
{
    private const string DefaultPlatformUrl = "https://quantum.io-vii.com/";

    public static QuantumMarketplaceOptions FromEnvironment()
    {
        var configuredAddress = Environment.GetEnvironmentVariable("QUANTUM_PLATFORM_URL");
        var address = string.IsNullOrWhiteSpace(configuredAddress)
            ? DefaultPlatformUrl
            : configuredAddress.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseAddress)
            || baseAddress.Scheme is not ("http" or "https"))
        {
            baseAddress = new Uri(DefaultPlatformUrl);
        }
        var configuredVersion = Environment.GetEnvironmentVariable("QUANTUM_VERSION");
        var quantumVersion = string.IsNullOrWhiteSpace(configuredVersion)
            ? "0.1.0"
            : configuredVersion.Trim();
        return new QuantumMarketplaceOptions(baseAddress, quantumVersion);
    }
}
