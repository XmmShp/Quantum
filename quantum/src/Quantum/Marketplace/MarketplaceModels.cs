namespace Quantum.Marketplace;

[Flags]
public enum MarketplaceUserRoles
{
    None = 0,
    User = 1,
    Developer = 2,
    Reviewer = 4,
    Admin = 8
}

public sealed record MarketplaceUser(
    string UserId,
    string Username,
    string Email,
    MarketplaceUserRoles Roles);

public sealed record MarketplaceLogin(
    string AccessToken,
    DateTime ExpiresAtUtc,
    MarketplaceUser User);

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
    long DownloadCount,
    string? ReleaseId = null,
    string? PluginId = null);

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

public enum MarketplaceReleaseState
{
    Pending = 1,
    Published = 2,
    Rejected = 3
}

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
