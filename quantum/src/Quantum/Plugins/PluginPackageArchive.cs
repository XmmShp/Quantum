using System.IO.Compression;

namespace Quantum.Plugins;

public static class PluginPackageLimits
{
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;

    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;

    public const int MaximumEntries = 20_000;

    public const int MaximumPlugins = 256;
}

internal static class PluginPackageArchive
{
    public static async Task<string> ExtractAsync(
        Stream archiveStream,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        Directory.CreateDirectory(stagingRoot);
        var archivePath = Path.Combine(stagingRoot, "package.zip");
        await CopyArchiveAsync(archiveStream, archivePath, cancellationToken).ConfigureAwait(false);

        var contentsRoot = Path.Combine(stagingRoot, "contents");
        Directory.CreateDirectory(contentsRoot);
        var contentsRootPrefix = Path.GetFullPath(contentsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(File.OpenRead(archivePath), ZipArchiveMode.Read);
        if (archive.Entries.Count == 0 || archive.Entries.Count > PluginPackageLimits.MaximumEntries)
        {
            throw new InvalidDataException("插件 ZIP 为空或文件数量超过限制。");
        }

        long expandedBytes = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizeEntryPath(entry.FullName);
            if (!paths.Add(normalizedPath.TrimEnd('/')))
            {
                throw new InvalidDataException($"插件 ZIP 包含重复路径 '{normalizedPath}'。");
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException($"插件 ZIP 不能包含符号链接 '{normalizedPath}'。");
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > PluginPackageLimits.MaximumExpandedBytes)
            {
                throw new InvalidDataException("插件 ZIP 解压后的大小超过限制。");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(
                contentsRoot,
                normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(contentsRootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"插件 ZIP 包含不安全路径 '{normalizedPath}'。");
            }

            if (normalizedPath.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(destinationDirectory);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await CopyEntryAsync(source, destination, entry.Length, normalizedPath, cancellationToken)
                .ConfigureAwait(false);
        }

        return contentsRoot;
    }

    public static IReadOnlyList<PluginCandidate> DiscoverPlugins(
        string contentsRoot,
        JsonPluginManifestReader manifestReader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentsRoot);
        ArgumentNullException.ThrowIfNull(manifestReader);

        var manifestPaths = Directory
            .EnumerateFiles(contentsRoot, "*", SearchOption.AllDirectories)
            .Where(static path => string.Equals(
                Path.GetFileName(path),
                "plugin.json",
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (manifestPaths.Length == 0)
        {
            throw new InvalidDataException("ZIP 中未找到插件目录；每个插件文件夹的根目录必须包含 plugin.json。");
        }

        if (manifestPaths.Length > PluginPackageLimits.MaximumPlugins)
        {
            throw new InvalidDataException("ZIP 中的插件数量超过限制。");
        }

        var pluginRoots = manifestPaths
            .Select(static path => Path.GetDirectoryName(path)!)
            .ToArray();
        foreach (var root in pluginRoots)
        {
            var rootPrefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (pluginRoots.Any(other => !string.Equals(other, root, StringComparison.Ordinal)
                && Path.GetFullPath(other).StartsWith(rootPrefix, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("ZIP 中的插件目录不能互相嵌套。");
            }
        }

        return pluginRoots.Select(manifestReader.Read).ToArray();
    }

    private static async Task CopyArchiveAsync(
        Stream source,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > PluginPackageLimits.MaximumArchiveBytes)
            {
                throw new InvalidDataException("插件 ZIP 大小超过限制。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (copied == 0)
        {
            throw new InvalidDataException("插件 ZIP 为空。");
        }
    }

    private static async Task CopyEntryAsync(
        Stream source,
        Stream destination,
        long expectedLength,
        string entryPath,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > expectedLength)
            {
                throw new InvalidDataException($"插件 ZIP 条目 '{entryPath}' 解压长度超过声明值。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (copied != expectedLength)
        {
            throw new InvalidDataException($"插件 ZIP 条目 '{entryPath}' 解压长度不一致。");
        }
    }

    private static string NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"插件 ZIP 包含不安全路径 '{path}'。");
        }

        var isDirectory = path.EndsWith("/", StringComparison.Ordinal);
        var comparablePath = isDirectory ? path[..^1] : path;
        if (string.IsNullOrWhiteSpace(comparablePath)
            || comparablePath.Split('/').Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException($"插件 ZIP 包含不安全路径 '{path}'。");
        }

        return isDirectory ? comparablePath + "/" : comparablePath;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
}
