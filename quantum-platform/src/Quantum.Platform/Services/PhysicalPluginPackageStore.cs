using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Quantum.Platform.Application;
using Quantum.Platform.Domain;

namespace Quantum.Platform;

public sealed partial class PhysicalPluginPackageStore : IPluginPackageStore
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

        var hasLegacyEntry = !string.IsNullOrWhiteSpace(manifest.EntryAssembly);
        if (hasLegacyEntry && manifest.Runtime is not null)
        {
            throw new InvalidDataException("plugin.json cannot declare both entryAssembly and runtime.");
        }

        if (hasLegacyEntry)
        {
            ValidateDotNetEntry(manifest.EntryAssembly!, files);
        }
        else if (manifest.Runtime is null)
        {
            throw new InvalidDataException("plugin.json must declare entryAssembly or runtime.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Runtime.Entry)
                || string.IsNullOrWhiteSpace(manifest.Runtime.Kind))
            {
                throw new InvalidDataException("plugin.json runtime kind and entry are required.");
            }

            var entry = NormalizeArchivePath(manifest.Runtime.Entry);
            switch (manifest.Runtime.Kind.Trim().ToLowerInvariant())
            {
                case "dotnet":
                    ValidateDotNetEntry(entry, files);
                    break;
                case "web" when (entry.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                    || entry.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
                    && files.Contains($"wwwroot/{entry}"):
                    break;
                case "web":
                    throw new InvalidDataException(
                        "plugin.json Web runtime entry must name a JavaScript module under wwwroot.");
                default:
                    throw new InvalidDataException($"Unknown plugin runtime kind '{manifest.Runtime.Kind}'.");
            }
        }

        if (manifest.Database is not null)
        {
            ValidateDatabaseMigrations(manifest.Database.Migrations, archive.Entries);
        }
    }

    private static void ValidateDatabaseMigrations(
        string? configuredPath,
        IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)
            || !string.Equals(configuredPath, configuredPath.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("plugin.json database.migrations must name a relative directory.");
        }

        var directory = configuredPath.StartsWith("./", StringComparison.Ordinal)
            ? configuredPath[2..]
            : configuredPath;
        var normalizedDirectory = NormalizeArchivePath(directory);
        if (normalizedDirectory.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("plugin.json database.migrations must not end with a slash.");
        }

        var prefix = normalizedDirectory + "/";
        var migrationEntries = entries
            .Where(entry =>
            {
                var path = NormalizeArchivePath(entry.FullName);
                return !path.EndsWith("/", StringComparison.Ordinal)
                    && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        if (migrationEntries.Length == 0)
        {
            throw new InvalidDataException(
                $"Database migrations directory '{configuredPath}' must contain at least one SQL migration.");
        }

        var sequences = new HashSet<BigInteger>();
        foreach (var entry in migrationEntries)
        {
            var relativePath = NormalizeArchivePath(entry.FullName)[prefix.Length..];
            if (relativePath.Contains('/', StringComparison.Ordinal))
            {
                throw new InvalidDataException("Database migrations directory cannot contain nested directories.");
            }

            var match = MigrationFileName().Match(relativePath);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"Migration file '{relativePath}' must use '<number>_<description>.sql'.");
            }

            if (entry.Length == 0)
            {
                throw new InvalidDataException($"Migration file '{relativePath}' is empty.");
            }

            var sequence = BigInteger.Parse(
                match.Groups["sequence"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (!sequences.Add(sequence))
            {
                throw new InvalidDataException(
                    $"Database migrations contain duplicate sequence '{match.Groups["sequence"].Value}'.");
            }
        }
    }

    private static void ValidateDotNetEntry(string entry, IReadOnlySet<string> files)
    {
        if (!string.Equals(Path.GetFileName(entry), entry, StringComparison.Ordinal)
            || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || !files.Contains(entry))
        {
            throw new InvalidDataException("plugin.json .NET runtime entry must name a DLL at the package root.");
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
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException($"Unsafe plugin package path '{path}'.");
        }

        var comparablePath = path.EndsWith("/", StringComparison.Ordinal)
            ? path[..^1]
            : path;
        if (string.IsNullOrWhiteSpace(comparablePath)
            || path.Contains('\\')
            || path.Contains(':')
            || path.StartsWith('/')
            || comparablePath.Split('/').Any(segment => segment.Length == 0 || segment is ".." or "."))
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

        public PluginRuntimeEnvelope? Runtime { get; init; }

        public PluginDatabaseEnvelope? Database { get; init; }
    }

    private sealed record PluginRuntimeEnvelope
    {
        public string? Kind { get; init; }

        public string? Entry { get; init; }
    }

    private sealed record PluginDatabaseEnvelope
    {
        public string? Migrations { get; init; }
    }

    [GeneratedRegex("^(?<sequence>[0-9]+)_[A-Za-z0-9][A-Za-z0-9._-]*\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationFileName();
}
