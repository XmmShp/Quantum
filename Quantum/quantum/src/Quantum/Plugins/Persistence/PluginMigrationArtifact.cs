using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Quantum.Plugins.Persistence;

internal static partial class PluginMigrationArtifact
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyList<PluginMigrationFile> Discover(
        string pluginRootPath,
        PluginDatabaseDefinition database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRootPath);
        ArgumentNullException.ThrowIfNull(database);

        var directoryPath = Path.Combine(
            Path.GetFullPath(pluginRootPath),
            database.Migrations.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Database migrations directory '{database.Migrations}' was not found.");
        }

        var nestedDirectory = Directory
            .EnumerateDirectories(directoryPath)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (nestedDirectory is not null)
        {
            throw new InvalidDataException(
                $"Database migrations directory cannot contain nested directory '{Path.GetFileName(nestedDirectory)}'.");
        }

        var migrations = new List<PluginMigrationFile>();
        var sequences = new HashSet<BigInteger>();
        foreach (var filePath in Directory.EnumerateFiles(directoryPath).Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(filePath);
            var match = MigrationFileName().Match(fileName);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"Migration file '{fileName}' must use '<number>_<description>.sql'.");
            }

            var sequence = BigInteger.Parse(
                match.Groups["sequence"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (!sequences.Add(sequence))
            {
                throw new InvalidDataException(
                    $"Database migrations contain duplicate sequence '{match.Groups["sequence"].Value}'.");
            }

            if (new FileInfo(filePath).Length == 0)
            {
                throw new InvalidDataException($"Migration file '{fileName}' is empty.");
            }

            migrations.Add(new PluginMigrationFile(fileName, sequence, filePath));
        }

        if (migrations.Count == 0)
        {
            throw new InvalidDataException("Database migrations directory must contain at least one SQL migration.");
        }

        return migrations
            .OrderBy(static migration => migration.Sequence)
            .ThenBy(static migration => migration.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<PluginMigrationScript> ReadAsync(
        PluginMigrationFile migration,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(migration.FullPath, cancellationToken).ConfigureAwait(false);
        string sql;
        try
        {
            sql = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"Migration file '{migration.Name}' must be valid UTF-8.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidDataException($"Migration file '{migration.Name}' is empty.");
        }

        return new PluginMigrationScript(
            migration.Name,
            sql,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [GeneratedRegex("^(?<sequence>[0-9]+)_[A-Za-z0-9][A-Za-z0-9._-]*\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationFileName();
}

internal sealed record PluginMigrationFile(string Name, BigInteger Sequence, string FullPath);

internal sealed record PluginMigrationScript(string Name, string Sql, string Sha256);
