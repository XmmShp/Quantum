namespace Quantum.ExtensionMarket.Authentication;

public sealed class MarketJwtOptions
{
    public const string SectionName = "ExtensionMarket:Jwt";

    public string Issuer { get; set; } = "Quantum.ExtensionMarket";
    public string Audience { get; set; } = "Quantum.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 60;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(Audience);
        if (SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"{SectionName}:SigningKey must contain at least 32 characters and must be supplied outside source control.");
        }

        if (LifetimeMinutes is < 5 or > 43_200)
        {
            throw new InvalidOperationException($"{SectionName}:LifetimeMinutes must be between 5 and 43200.");
        }
    }
}
