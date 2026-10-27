using Microsoft.Extensions.Options;
using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Quantum.Platform.Tests;

public sealed class S3StorageTests
{
    [Fact]
    public async Task ChunksCanBeRetriedOutOfOrderAndCompletedOnAnotherInstance()
    {
        var objects = new MemoryObjects();
        var bytes = RandomNumberGenerator.GetBytes(MobileAttachments.ChunkBytes + 51);
        var file = Attachment(bytes);
        var first = new S3MobileAttachmentStore(objects);
        await first.WriteAsync("account", file, MobileAttachments.ChunkBytes, bytes[MobileAttachments.ChunkBytes..], default);
        await Assert.ThrowsAsync<BridgeException>(() => first.CompleteAsync("account", file, default));
        await first.WriteAsync("account", file, 0, bytes[..MobileAttachments.ChunkBytes], default);
        await first.WriteAsync("account", file, 0, bytes[..MobileAttachments.ChunkBytes], default);
        var second = new S3MobileAttachmentStore(objects);
        await second.CompleteAsync("account", file, default);
        await second.CompleteAsync("account", file, default);
        Assert.Equal(bytes[..MobileAttachments.ChunkBytes], await second.ReadAsync("account", file, 0, default));
        Assert.Equal(bytes[MobileAttachments.ChunkBytes..], await second.ReadAsync("account", file, MobileAttachments.ChunkBytes, default));
        Assert.Single(objects.Values);
        await Assert.ThrowsAsync<FileNotFoundException>(() => second.ReadAsync("other-account", file, 0, default));
        await Assert.ThrowsAsync<BridgeException>(() => second.WriteAsync("account", file, 1, [1], default));
    }

    [Fact]
    public async Task CorruptChunksAreNeverPublishedAndExpiredAttachmentsAreRemoved()
    {
        var objects = new MemoryObjects();
        var bytes = RandomNumberGenerator.GetBytes(51);
        var file = Attachment(bytes);
        var store = new S3MobileAttachmentStore(objects);
        await store.WriteAsync("account", file, 0, new byte[bytes.Length], default);
        await Assert.ThrowsAsync<BridgeException>(() => store.CompleteAsync("account", file, default));
        await store.WriteAsync("account", file, 0, bytes, default);
        await store.CompleteAsync("account", file, default);
        var key = Assert.Single(objects.Values).Key;
        objects.Values[key] = objects.Values[key] with { LastModified = DateTimeOffset.UtcNow.AddHours(-25) };
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.ReadAsync("account", file, 0, default));
        Assert.Empty(objects.Values);
    }

    [Fact]
    public async Task ExistingMigratedAttachmentIsReadableWithoutUploadChunks()
    {
        var objects = new MemoryObjects();
        var bytes = RandomNumberGenerator.GetBytes(51);
        var file = Attachment(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("account")));
        await objects.PutAsync($"mobile-attachments/{hash}/{file.Id}.bin", bytes, true, default);
        var store = new S3MobileAttachmentStore(objects);
        await store.CompleteAsync("account", file, default);
        Assert.Equal(bytes, await store.ReadAsync("account", file, 0, default));
    }

    [Fact]
    public async Task PluginPackagesKeepTheirPathsValidationAndCreateOnlySemantics()
    {
        var objects = new MemoryObjects();
        var store = new S3PluginPackageStore(objects, Options.Create(new PluginStorageOptions()));
        var bytes = Archive();
        var stored = await store.SaveAsync("quantum.plugin.test", "1.0.0", Convert.ToBase64String(bytes), null, default);
        Assert.Equal("quantum.plugin.test/1.0.0.zip", stored.RelativePath);
        Assert.Equal(bytes, await new S3PluginPackageStore(objects, Options.Create(new PluginStorageOptions())).ReadAsync(stored.RelativePath, default));
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync("quantum.plugin.test", "1.0.0", Convert.ToBase64String(bytes), null, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync("quantum.plugin.test", "1.0.0", Convert.ToBase64String(bytes), "bad-hash", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync("quantum.plugin.test", "1.0.0", Convert.ToBase64String(Archive("../escape.js")), null, default));
        await store.DeleteAsync(stored.RelativePath, default);
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.ReadAsync(stored.RelativePath, default));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("/other")]
    [InlineData("plugin/../other")]
    [InlineData("plugin\\other")]
    public async Task S3KeysCannotEscapeTheConfiguredPrefix(string key)
    {
        using var store = new S3ObjectStore(Options.Create(new S3StorageOptions
        {
            ServiceUrl = "https://s3.example.com", AccessKeyId = "example", SecretAccessKey = "example"
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(key, default));
    }

    private static BridgeAttachment Attachment(byte[] bytes) => new(Guid.NewGuid().ToString("N"), "sample.bin",
        "application/octet-stream", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static byte[] Archive(string entry = "wwwroot/main.js")
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("plugin.json").Open()))
                writer.Write("""{"id":"quantum.plugin.test","version":"1.0.0","runtime":{"kind":"web","entry":"main.js"}}""");
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("export default {};");
        }
        return buffer.ToArray();
    }

    private sealed class MemoryObjects : IQuantumObjectStore
    {
        public Dictionary<string, StoredObject> Values { get; } = [];
        public Task PutAsync(string key, byte[] bytes, bool createOnly, CancellationToken cancellationToken)
        {
            if (createOnly && Values.ContainsKey(key)) throw new IOException("Already exists");
            Values[key] = new(bytes.ToArray(), DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
        public Task<StoredObject> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Values.TryGetValue(key, out var value) ? value : throw new FileNotFoundException());
        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
