using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public interface IPlatformCallerContext
{
    string? UserId { get; }
}

public interface IPlatformPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public interface IPlatformTokenIssuer
{
    IssuedAccessToken Issue(PlatformUser user);
}

public sealed record IssuedAccessToken(string AccessToken, DateTime ExpiresAtUtc);

public interface IPluginPackageStore
{
    Task<StoredPluginPackage> SaveAsync(
        string pluginId,
        string version,
        string archiveBase64,
        string? expectedSha256,
        CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record StoredPluginPackage(
    string RelativePath,
    long SizeBytes,
    string Sha256);

public sealed class PluginStorageOptions
{
    public const string SectionName = "QuantumPlatform:Storage";

    public string BasePath { get; set; } = "Files";
    public long MaxArchiveBytes { get; set; } = 256L * 1024 * 1024;
    public long MaxExpandedBytes { get; set; } = 1024L * 1024 * 1024;
    public int MaxEntries { get; set; } = 5000;
}
