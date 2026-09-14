namespace Quantum.Marketplace;

public sealed record MarketplacePlugin(
    string PluginId,
    string Name,
    string Description,
    string AuthorName,
    string[] Tags,
    MarketplaceRelease? LatestRelease);

public sealed record MarketplacePluginDetails(
    MarketplacePlugin Plugin,
    MarketplaceRelease[] Releases);

public sealed record MarketplaceRelease(
    string Version,
    string QuantumVersionSupport,
    string ReleaseNotes,
    int Status,
    long PackageSizeBytes,
    string PackageSha256,
    DateTime UploadedAtUtc,
    long DownloadCount);

public sealed record MarketplaceCompatibility(
    bool IsCompatible,
    string? MatchedReleaseVersion,
    string? QuantumVersionSupport);

public sealed record MarketplaceDownload(
    string FileName,
    string ContentType,
    byte[] Archive,
    long PackageSizeBytes,
    string PackageSha256);

public sealed class MarketplaceException : Exception
{
    public MarketplaceException(string message)
        : base(message)
    {
    }

    public MarketplaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
