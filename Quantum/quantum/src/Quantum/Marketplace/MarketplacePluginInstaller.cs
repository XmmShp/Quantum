using Quantum.Localization;
using Quantum.Plugins;

namespace Quantum.Marketplace;

public sealed class MarketplacePluginInstaller(
    IQuantumMarketplaceClient marketplace,
    IPluginRuntimeManager runtime,
    QuantumMarketplaceOptions options)
{
    public async Task<PluginOperationResult> InstallAsync(MarketplacePlugin plugin, CancellationToken cancellationToken = default)
    {
        var compatibility = await marketplace.CheckCompatibilityAsync(plugin.PluginId, options.QuantumVersion, cancellationToken);
        if (!compatibility.IsCompatible || compatibility.MatchedReleaseVersion is not { } version)
            return PluginOperationResult.Failure(AppText.Get("MarketplacePluginIncompatible", plugin.Name, options.QuantumVersion));

        var download = await marketplace.DownloadAsync(plugin.PluginId, version, cancellationToken);
        if (download.Archive.LongLength > PluginPackageLimits.MaximumArchiveBytes)
            throw new InvalidDataException(AppText.Get("TheDownloadedPackageExceedsTheLocalInstallationSize"));

        await using var stream = new MemoryStream(download.Archive, writable: false);
        var preview = await runtime.PrepareInstallAsync(stream, download.FileName, cancellationToken);
        try
        {
            if (!preview.CanInstall || preview.PreviewId is not { } previewId)
            {
                var reason = string.Join(" ", preview.Issues.Select(issue => issue.Reason));
                return PluginOperationResult.Failure(string.IsNullOrWhiteSpace(reason) ? AppText.Get("LocalPreflightFailed") : reason);
            }
            if (preview.Plugins.Count != 1 || preview.Plugins[0].PluginId != plugin.PluginId || preview.Plugins[0].PackageVersion != version)
                return PluginOperationResult.Failure(AppText.Get("MarketplacePackageIdentityMismatch"));
            return await runtime.InstallAsync(previewId, cancellationToken);
        }
        finally
        {
            if (preview.PreviewId is { } previewId)
                await runtime.CancelInstallAsync(previewId, CancellationToken.None);
        }
    }
}
