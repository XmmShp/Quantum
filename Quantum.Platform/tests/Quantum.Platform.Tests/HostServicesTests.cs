using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quantum.Platform.Application;
using Quantum.Platform.Domain;
using NOF.Domain;

namespace Quantum.Platform.Tests;

public sealed class HostServicesTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "quantum-platform-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PasswordHasher_RoundTripsWithoutStoringPlaintext()
    {
        var hasher = new Pbkdf2PlatformPasswordHasher();

        var hash = hasher.Hash("correct horse battery staple");

        Assert.DoesNotContain("correct horse", hash, StringComparison.Ordinal);
        Assert.True(hasher.Verify(hash, "correct horse battery staple"));
        Assert.False(hasher.Verify(hash, "incorrect password"));
    }

    [Fact]
    public void CredentialClientKeyPair_SeparatesPublicAndPrivateMaterial()
    {
        var keySet = CredentialClientKeyPairGenerator.Generate();
        using var publicDocument = JsonDocument.Parse(keySet.JsonWebKeySet);
        using var privateDocument = JsonDocument.Parse(keySet.PrivateJsonWebKeySet);
        var publicKey = publicDocument.RootElement.GetProperty("keys")[0];
        var privateKey = privateDocument.RootElement.GetProperty("keys")[0];

        Assert.Equal("RSA", publicKey.GetProperty("kty").GetString());
        Assert.Equal(publicKey.GetProperty("kid").GetString(), privateKey.GetProperty("kid").GetString());
        Assert.False(publicKey.TryGetProperty("d", out _));
        Assert.True(privateKey.TryGetProperty("d", out _));
        Assert.Equal("[REDACTED]", keySet.ToString());
    }


    [Fact]
    public async Task DevelopmentReviewer_ApprovesWithoutCallingAgentFn()
    {
        var idGenerator = new SnowflakeIdGenerator();
        var listing = PluginListing.Create(
            "quantum.plugin.local",
            "Local plugin",
            "Development package",
            PlatformUserId.Of(1),
            [],
            idGenerator);
        var release = PluginRelease.Create(
            listing.Id,
            "1.0.0",
            ">=0.1.0",
            string.Empty,
            "package.zip",
            128,
            new string('a', 64),
            idGenerator);
        var reviewer = new DevelopmentAutoApprovePluginReleaseReviewer();

        var taskId = await reviewer.StartAsync(listing, release, [1, 2, 3], CancellationToken.None);
        var result = await reviewer.WaitAsync(taskId, CancellationToken.None);

        Assert.Equal(AutomatedPluginReviewDecision.Approve, result.Decision);
        Assert.StartsWith("development-auto-approve-", result.TaskId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageStore_ValidatesManifestAndPersistsAtomically()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {"id":"quantum.plugin.example","version":"1.0.0","entryAssembly":"Example.dll"}
                """),
            ("Example.dll", "not-a-real-assembly"));

        var stored = await store.SaveAsync(
            "quantum.plugin.example",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None);
        var restored = await store.ReadAsync(stored.RelativePath, CancellationToken.None);

        Assert.Equal("quantum.plugin.example/1.0.0.zip", stored.RelativePath);
        Assert.Equal(archive, restored);
        Assert.Equal(64, stored.Sha256.Length);
    }

    [Fact]
    public async Task PackageStore_RejectsArchiveTraversal()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {"id":"quantum.plugin.example","version":"1.0.0","entryAssembly":"Example.dll"}
                """),
            ("Example.dll", "not-a-real-assembly"),
            ("../escape.txt", "unsafe"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            "quantum.plugin.example",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task PackageStore_AcceptsWebPluginWithoutAssembly()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {
                  "id":"quantum.plugin.web",
                  "version":"1.0.0",
                  "runtime":{"kind":"web","entry":"dist/plugin.js"}
                }
                """),
            ("wwwroot/dist/plugin.js", "export default {};"));

        var stored = await store.SaveAsync(
            "quantum.plugin.web",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None);

        Assert.Equal("quantum.plugin.web/1.0.0.zip", stored.RelativePath);
    }

    [Fact]
    public async Task PackageStore_AcceptsLanguageIndependentSqlMigrations()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {
                  "id":"quantum.plugin.web",
                  "version":"1.0.0",
                  "runtime":{"kind":"web","entry":"dist/plugin.js"},
                  "database":{"migrations":"./migrations"}
                }
                """),
            ("wwwroot/dist/plugin.js", "export default {};"),
            ("migrations/001_init.sql", "CREATE TABLE notes (id TEXT PRIMARY KEY);"),
            ("migrations/002_add_index.sql", "CREATE INDEX notes_id ON notes (id);"));

        var stored = await store.SaveAsync(
            "quantum.plugin.web",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None);

        Assert.Equal("quantum.plugin.web/1.0.0.zip", stored.RelativePath);
    }

    [Fact]
    public async Task PackageStore_RejectsInvalidMigrationArtifact()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {
                  "id":"quantum.plugin.example",
                  "version":"1.0.0",
                  "entryAssembly":"Example.dll",
                  "database":{"migrations":"migrations"}
                }
                """),
            ("Example.dll", "not-a-real-assembly"),
            ("migrations/init.sql", "SELECT 1;"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            "quantum.plugin.example",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task PackageStore_RejectsPlatformSpecificWebEntryPath()
    {
        var store = CreateStore();
        var archive = CreateArchive(
            ("plugin.json", """
                {
                  "id":"quantum.plugin.web",
                  "version":"1.0.0",
                  "runtime":{"kind":"web","entry":"C:/plugin.js"}
                }
                """),
            ("wwwroot/C:/plugin.js", "export default {};"));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            "quantum.plugin.web",
            "1.0.0",
            Convert.ToBase64String(archive),
            null,
            CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private PhysicalPluginPackageStore CreateStore()
        => new(Options.Create(new PluginStorageOptions
        {
            BasePath = rootPath,
            MaxArchiveBytes = 1024 * 1024,
            MaxExpandedBytes = 2 * 1024 * 1024,
            MaxEntries = 100
        }));

    private static byte[] CreateArchive(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(file.Content);
            }
        }

        return stream.ToArray();
    }
}
