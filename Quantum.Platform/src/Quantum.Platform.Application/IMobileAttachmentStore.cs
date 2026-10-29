using Quantum.Mobile.Internal;

namespace Quantum.Platform.Application;

public interface IMobileAttachmentStore
{
    Task WriteAsync(string account, BridgeAttachment file, long offset, byte[] data, CancellationToken cancellationToken);
    Task CompleteAsync(string account, BridgeAttachment file, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(string account, BridgeAttachment file, long offset, CancellationToken cancellationToken);
}
