using Microsoft.Extensions.Options;
using Quantum.Platform.Application;
using Quantum.Platform.Domain;
using System.Security.Cryptography;

namespace Quantum.Platform;

public sealed class S3PluginPackageStore(IQuantumObjectStore objects, IOptions<PluginStorageOptions> configured) : IPluginPackageStore
{
    public async Task<StoredPluginPackage> SaveAsync(string pluginId, string version, string archiveBase64,
        string? expectedSha256, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        pluginId = PluginListing.NormalizePluginId(pluginId);
        version = PluginRelease.NormalizeVersion(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveBase64);
        if (options.MaxArchiveBytes <= 0 || options.MaxExpandedBytes <= 0 || options.MaxEntries <= 0)
            throw new InvalidOperationException("Plugin storage limits must be positive.");
        if (archiveBase64.Length > checked(((options.MaxArchiveBytes + 2) / 3) * 4 + 16))
            throw new InvalidDataException("The plugin ZIP archive exceeds the configured size limit.");
        var bytes = Convert.FromBase64String(archiveBase64);
        if (bytes.LongLength == 0 || bytes.LongLength > options.MaxArchiveBytes)
            throw new InvalidDataException("The plugin ZIP archive is empty or exceeds the configured size limit.");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.IsNullOrWhiteSpace(expectedSha256) && !string.Equals(expectedSha256.Trim(), sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The plugin package SHA-256 does not match ExpectedSha256.");
        PhysicalPluginPackageStore.ValidateArchive(bytes, pluginId, version, options);
        var path = $"{pluginId}/{version}.zip";
        await objects.PutAsync(path, bytes, createOnly: true, cancellationToken);
        return new StoredPluginPackage(path, bytes.LongLength, sha256);
    }

    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken)
        => (await objects.GetAsync(relativePath, cancellationToken)).Bytes;

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
        => objects.DeleteAsync(relativePath, cancellationToken);
}
