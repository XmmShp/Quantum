using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quantum.ExtensionMarket.Application;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket;

public sealed class PhysicalPluginPackageStore : IPluginPackageStore
{
    private readonly string rootPath;
    private readonly PluginStorageOptions options;

    public PhysicalPluginPackageStore(IOptions<PluginStorageOptions> options)
    {
        this.options = options.Value;
        if (this.options.MaxArchiveBytes <= 0 || this.options.MaxExpandedBytes <= 0 || this.options.MaxEntries <= 0)
        {
            throw new InvalidOperationException("Plugin storage limits must be positive.");
        }

        rootPath = Path.GetFullPath(Path.IsPathRooted(this.options.BasePath)
            ? this.options.BasePath
            : Path.Combine(AppContext.BaseDirectory, this.options.BasePath));
        Directory.CreateDirectory(rootPath);
    }

    public async Task<StoredPluginPackage> SaveAsync(
        string pluginId,
        string version,
        string archiveBase64,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        pluginId = PluginListing.NormalizePluginId(pluginId);
        version = PluginRelease.NormalizeVersion(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveBase64);
        var maximumEncodedLength = checked(((options.MaxArchiveBytes + 2) / 3) * 4 + 16);
        if (archiveBase64.Length > maximumEncodedLength)
        {
            throw new InvalidDataException("The plugin ZIP archive exceeds the configured size limit.");
        }

        var archiveBytes = Convert.FromBase64String(archiveBase64);
        if (archiveBytes.LongLength == 0 || archiveBytes.LongLength > options.MaxArchiveBytes)
        {
            throw new InvalidDataException("The plugin ZIP archive is empty or exceeds the configured size limit.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(expectedSha256.Trim(), sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The plugin package SHA-256 does not match ExpectedSha256.");
        }

        ValidateArchive(archiveBytes, pluginId, version);
        var relativePath = $"{pluginId}/{version}.zip";
        var destinationPath = ResolvePath(relativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(destinationDirectory, $".{version}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, archiveBytes, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }

        return new StoredPluginPackage(relativePath, archiveBytes.LongLength, sha256);
    }

    public Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken)
        => File.ReadAllBytesAsync(ResolvePath(relativePath), cancellationToken);

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(relativePath);
        File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && !string.Equals(directory, rootPath, StringComparison.Ordinal))
        {
            try
            {
                Directory.Delete(directory, recursive: false);
            }
            catch (IOException)
            {
            }
        }

        return Task.CompletedTask;
    }

    private void ValidateArchive(byte[] archiveBytes, string pluginId, string version)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0 || archive.Entries.Count > options.MaxEntries)
        {
            throw new InvalidDataException("The plugin ZIP archive is empty or contains too many entries.");
        }

        long expandedSize = 0;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var path = NormalizeArchivePath(entry.FullName);
            expandedSize = checked(expandedSize + entry.Length);
            if (expandedSize > options.MaxExpandedBytes)
            {
                throw new InvalidDataException("The expanded plugin package exceeds the configured size limit.");
            }

            if (path.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!files.Add(path))
            {
                throw new InvalidDataException($"The plugin package contains duplicate path '{path}'.");
            }
        }

        var manifestEntry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(NormalizeArchivePath(entry.FullName), "plugin.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null || manifestEntry.Length == 0 || manifestEntry.Length > 1024 * 1024)
        {
            throw new InvalidDataException("The plugin package root must contain a non-empty plugin.json under 1 MiB.");
        }

        PluginManifestEnvelope manifest;
        try
        {
            using var manifestStream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<PluginManifestEnvelope>(
                manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidDataException("plugin.json is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("plugin.json is not valid JSON.", exception);
        }

        if (!string.Equals(manifest.Id, pluginId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("plugin.json id and version must match the requested plugin release.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) ||
            !string.Equals(Path.GetFileName(manifest.EntryAssembly), manifest.EntryAssembly, StringComparison.Ordinal) ||
            !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !files.Contains(manifest.EntryAssembly))
        {
            throw new InvalidDataException("plugin.json entryAssembly must name a DLL at the package root.");
        }
    }

    private string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Plugin package paths must be relative.");
        }

        var candidate = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The plugin package path escapes the configured storage root.");
        }

        return candidate;
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/') ||
            path.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new InvalidDataException($"Unsafe plugin package path '{path}'.");
        }

        return path;
    }

    private sealed record PluginManifestEnvelope
    {
        public string? Id { get; init; }
        public string? Version { get; init; }
        public string? EntryAssembly { get; init; }
    }
}
