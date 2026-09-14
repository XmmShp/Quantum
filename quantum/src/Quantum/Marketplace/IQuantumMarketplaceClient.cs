namespace Quantum.Marketplace;

public interface IQuantumMarketplaceClient
{
    Uri BaseAddress { get; }

    Task<IReadOnlyList<MarketplacePlugin>> ListPluginsAsync(
        string? search = null,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    Task<MarketplacePluginDetails> GetPluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCompatibility> CheckCompatibilityAsync(
        string pluginId,
        string quantumVersion,
        CancellationToken cancellationToken = default);

    Task<MarketplaceDownload> DownloadAsync(
        string pluginId,
        string version,
        CancellationToken cancellationToken = default);
}
