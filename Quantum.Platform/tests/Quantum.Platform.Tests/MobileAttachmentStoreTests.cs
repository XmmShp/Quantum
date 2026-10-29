using Microsoft.Extensions.Options;
using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using System.Security.Cryptography;

namespace Quantum.Platform.Tests;

public sealed class MobileAttachmentStoreTests
{
    [Fact]
    public async Task ReorderedAndRetriedChunksSurviveStoreRecreationAndVerifyTheFinalHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "quantum-attachment-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new PluginStorageOptions { BasePath = root });
            var store = new PhysicalMobileAttachmentStore(options);
            var bytes = RandomNumberGenerator.GetBytes(MobileAttachments.ChunkBytes + 51);
            var file = new BridgeAttachment(Guid.NewGuid().ToString("N"), "sample.bin", "application/octet-stream", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            await store.WriteAsync("account", file, MobileAttachments.ChunkBytes, bytes[MobileAttachments.ChunkBytes..], default);
            await Assert.ThrowsAsync<BridgeException>(() => store.CompleteAsync("account", file, default));
            await store.WriteAsync("account", file, 0, bytes[..MobileAttachments.ChunkBytes], default);
            await store.WriteAsync("account", file, 0, bytes[..MobileAttachments.ChunkBytes], default);
            store = new(options);
            await store.CompleteAsync("account", file, default);
            Assert.Equal(bytes[..MobileAttachments.ChunkBytes], await store.ReadAsync("account", file, 0, default));
            Assert.Equal(bytes[MobileAttachments.ChunkBytes..], await store.ReadAsync("account", file, MobileAttachments.ChunkBytes, default));
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.ReadAsync("different-account", file, 0, default));
            await Assert.ThrowsAsync<BridgeException>(() => store.WriteAsync("account", file, 1, [1], default));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
