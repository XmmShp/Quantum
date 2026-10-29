using Microsoft.Extensions.Options;
using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using System.Security.Cryptography;
using System.Text;

namespace Quantum.Platform;

public sealed class PhysicalMobileAttachmentStore : IMobileAttachmentStore
{
    private readonly string _root;
    private long _cleanupAt;

    public PhysicalMobileAttachmentStore(IOptions<PluginStorageOptions> options)
    {
        var basePath = options.Value.BasePath;
        _root = Path.GetFullPath(Path.Combine(Path.IsPathRooted(basePath) ? basePath : Path.Combine(AppContext.BaseDirectory, basePath), "mobile-attachments"));
        Directory.CreateDirectory(_root);
    }

    private string FilePath(string account, BridgeAttachment file)
    {
        MobileAttachments.Validate(file);
        var directory = Path.Combine(_root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account))));
        Directory.CreateDirectory(directory);
        if (DateTime.UtcNow.Ticks > Interlocked.Read(ref _cleanupAt))
        {
            Interlocked.Exchange(ref _cleanupAt, DateTime.UtcNow.AddHours(1).Ticks);
            foreach (var path in Directory.EnumerateFiles(_root, "*.bin", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-24)) File.Delete(path);
            }
        }
        return Path.Combine(directory, file.Id + ".bin");
    }

    public async Task WriteAsync(string account, BridgeAttachment file, long offset, byte[] data, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset % MobileAttachments.ChunkBytes != 0 || data.Length != Math.Min(MobileAttachments.ChunkBytes, file.Size - offset))
            throw new BridgeException("mobile_attachment_invalid", "Invalid attachment chunk.");
        await using var stream = new FileStream(FilePath(account, file), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 81920, true);
        if (stream.Length != file.Size) stream.SetLength(file.Size);
        stream.Position = offset;
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task CompleteAsync(string account, BridgeAttachment file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(FilePath(account, file));
        if (stream.Length != file.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) != file.Sha256)
            throw new BridgeException("mobile_attachment_invalid", "Attachment is incomplete or corrupt.");
    }

    public async Task<byte[]> ReadAsync(string account, BridgeAttachment file, long offset, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset >= file.Size || offset % MobileAttachments.ChunkBytes != 0)
            throw new BridgeException("mobile_attachment_invalid", "Invalid attachment offset.");
        await using var stream = File.OpenRead(FilePath(account, file));
        stream.Position = offset;
        var bytes = new byte[(int)Math.Min(MobileAttachments.ChunkBytes, file.Size - offset)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}
