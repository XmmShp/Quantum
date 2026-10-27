using System.Security.Cryptography;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

internal static class OutgoingAttachmentStore
{
    private static string Root => Path.Combine(FileSystem.AppDataDirectory, "OutgoingAttachments");
    public static string PathFor(BridgeAttachment file)
    {
        MobileAttachments.Validate(file);
        return Path.Combine(Root, file.Id + ".bin");
    }

    public static async Task<BridgeAttachment> SaveAsync(Stream source, string name, string contentType)
    {
        Directory.CreateDirectory(Root);
        foreach (var path in Directory.EnumerateFiles(Root, "*.bin"))
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-24)) File.Delete(path);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > MobileAttachments.MaximumFileBytes)
                throw new BridgeException("mobile_attachment_invalid", "Attachments must be 8 MiB or smaller.");
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        var file = new BridgeAttachment(Guid.NewGuid().ToString("N"), name, contentType, bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        MobileAttachments.Validate(file);
        await File.WriteAllBytesAsync(PathFor(file), bytes);
        return file;
    }
}
