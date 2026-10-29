using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Quantum.Platform.SkillDistribution;

internal sealed class QuantumPublishSkillPackage
{
    public const string Name = "publish-quantum-plugin";
    private static readonly DateTimeOffset ArchiveTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private QuantumPublishSkillPackage(SkillFile[] files)
    {
        Files = files;
        ContentSha256 = ComputeContentSha256(files);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                entry.LastWriteTime = ArchiveTimestamp;
                using var stream = entry.Open();
                stream.Write(file.Content);
            }
        }
        Archive = output.ToArray();
        ArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(Archive));
    }

    public SkillFile[] Files { get; }
    public string ContentSha256 { get; }
    public byte[] Archive { get; }
    public string ArchiveSha256 { get; }

    public static QuantumPublishSkillPackage Load(string webRootPath)
    {
        var root = Path.Combine(webRootPath, "skills", Name);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".br", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            .Select(path => SkillFile.Load(root, path))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        foreach (var required in new[] { "SKILL.md", "agents/openai.yaml", "references/ci.md", "scripts/update.py" })
        {
            if (!files.Any(file => file.Path == required)) throw new InvalidOperationException($"Skill is missing '{required}'.");
        }
        return new QuantumPublishSkillPackage(files);
    }

    public JsonObject CreateManifest() => new()
    {
        ["schemaVersion"] = 1,
        ["name"] = Name,
        ["contentSha256"] = ContentSha256,
        ["downloadUrl"] = $"./download?sha256={ContentSha256}",
        ["archiveSha256"] = ArchiveSha256,
        ["files"] = new JsonArray(Files.Select(file => (JsonNode)new JsonObject
        {
            ["path"] = file.Path,
            ["size"] = file.Content.Length,
            ["sha256"] = file.Sha256
        }).ToArray())
    };

    private static string ComputeContentSha256(IEnumerable<SkillFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            var path = Encoding.UTF8.GetBytes(file.Path);
            BinaryPrimitives.WriteInt64BigEndian(length, path.LongLength); hash.AppendData(length); hash.AppendData(path);
            BinaryPrimitives.WriteInt64BigEndian(length, file.Content.LongLength); hash.AppendData(length); hash.AppendData(file.Content);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

internal sealed record SkillFile(string Path, byte[] Content, string Sha256)
{
    public static SkillFile Load(string root, string path)
    {
        var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal)) throw new InvalidOperationException("Skill path escaped its root.");
        var content = File.ReadAllBytes(path);
        return new(relative, content, Convert.ToHexStringLower(SHA256.HashData(content)));
    }
}

internal sealed class QuantumPublishSkillPackageCache(IWebHostEnvironment environment)
{
    private readonly Lazy<QuantumPublishSkillPackage> package = new(
        () => QuantumPublishSkillPackage.Load(environment.WebRootPath),
        LazyThreadSafetyMode.ExecutionAndPublication);
    public QuantumPublishSkillPackage Value => package.Value;
}
