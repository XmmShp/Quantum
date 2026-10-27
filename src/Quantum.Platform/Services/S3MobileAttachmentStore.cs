using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using System.Security.Cryptography;
using System.Text;

namespace Quantum.Platform;

public sealed class S3MobileAttachmentStore(IQuantumObjectStore objects) : IMobileAttachmentStore
{
    private static string PathFor(string account, BridgeAttachment file)
    {
        MobileAttachments.Validate(file);
        return $"mobile-attachments/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account)))}/{file.Id}.bin";
    }

    private static string Chunk(string path, BridgeAttachment file, long offset)
        => $"{path}.chunks/{file.Sha256}/{offset / MobileAttachments.ChunkBytes:D6}";

    private static void Verify(byte[] bytes, BridgeAttachment file)
    {
        if (bytes.LongLength != file.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != file.Sha256)
            throw new BridgeException("mobile_attachment_invalid", "Attachment is incomplete or corrupt.");
    }

    public Task WriteAsync(string account, BridgeAttachment file, long offset, byte[] data, CancellationToken cancellationToken)
    {
        var path = PathFor(account, file);
        if (offset < 0 || offset >= file.Size || offset % MobileAttachments.ChunkBytes != 0
            || data.Length != Math.Min(MobileAttachments.ChunkBytes, file.Size - offset))
            throw new BridgeException("mobile_attachment_invalid", "Invalid attachment chunk.");
        return objects.PutAsync(Chunk(path, file, offset), data, createOnly: false, cancellationToken);
    }

    public async Task CompleteAsync(string account, BridgeAttachment file, CancellationToken cancellationToken)
    {
        var path = PathFor(account, file);
        try
        {
            Verify((await objects.GetAsync(path, cancellationToken)).Bytes, file);
            await DeleteChunksAsync(path, file, cancellationToken);
            return;
        }
        catch (FileNotFoundException) { }
        var bytes = new byte[checked((int)file.Size)];
        for (long offset = 0; offset < file.Size; offset += MobileAttachments.ChunkBytes)
        {
            StoredObject chunk;
            try { chunk = await objects.GetAsync(Chunk(path, file, offset), cancellationToken); }
            catch (FileNotFoundException) { throw new BridgeException("mobile_attachment_invalid", "Attachment is incomplete or corrupt."); }
            if (chunk.Bytes.Length != Math.Min(MobileAttachments.ChunkBytes, file.Size - offset))
                throw new BridgeException("mobile_attachment_invalid", "Attachment is incomplete or corrupt.");
            chunk.Bytes.CopyTo(bytes, checked((int)offset));
        }
        Verify(bytes, file);
        try { await objects.PutAsync(path, bytes, createOnly: true, cancellationToken); }
        catch (IOException) { Verify((await objects.GetAsync(path, cancellationToken)).Bytes, file); }
        await DeleteChunksAsync(path, file, cancellationToken);
    }

    private async Task DeleteChunksAsync(string path, BridgeAttachment file, CancellationToken cancellationToken)
    {
        for (long offset = 0; offset < file.Size; offset += MobileAttachments.ChunkBytes)
            await objects.DeleteAsync(Chunk(path, file, offset), cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string account, BridgeAttachment file, long offset, CancellationToken cancellationToken)
    {
        var path = PathFor(account, file);
        if (offset < 0 || offset >= file.Size || offset % MobileAttachments.ChunkBytes != 0)
            throw new BridgeException("mobile_attachment_invalid", "Invalid attachment offset.");
        var result = await objects.GetAsync(path, cancellationToken);
        if (result.LastModified < DateTimeOffset.UtcNow.AddHours(-24))
        {
            await objects.DeleteAsync(path, cancellationToken);
            throw new FileNotFoundException("The attachment has expired.");
        }
        Verify(result.Bytes, file);
        return result.Bytes.AsSpan(checked((int)offset), (int)Math.Min(MobileAttachments.ChunkBytes, file.Size - offset)).ToArray();
    }
}
