using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quantum.Mobile.Internal;

public sealed record BridgeAttachment(string Id, string Name, string ContentType, long Size, string Sha256);

public static class MobileAttachments
{
    public const int MaximumFiles = 4;
    public const int MaximumFileBytes = 8 * 1024 * 1024;
    public const int MaximumTotalBytes = 16 * 1024 * 1024;
    public const int ChunkBytes = 128 * 1024;

    public static BridgeAttachment[] Read(JsonElement value)
    {
        var files = MobileJson.Read<BridgeAttachment[]>(value);
        if (files.Length > MaximumFiles || files.Sum(f => f.Size) > MaximumTotalBytes)
            throw new BridgeException("mobile_attachment_invalid", "Attachment limits exceeded.");
        foreach (var file in files) Validate(file);
        if (files.Select(f => f.Id).Distinct().Count() != files.Length)
            throw new BridgeException("mobile_attachment_invalid", "Duplicate attachment.");
        return files;
    }

    public static void Validate(BridgeAttachment file)
    {
        if (!Guid.TryParseExact(file.Id, "N", out _) || string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 128 ||
            file.Name.Any(c => char.IsControl(c) || c is '/' or '\\') || file.Size is <= 0 or > MaximumFileBytes ||
            file.ContentType is null || !Regex.IsMatch(file.ContentType, "^[A-Za-z0-9.+-]{1,64}/[A-Za-z0-9.+-]{1,64}$") ||
            file.Sha256 is null || !Regex.IsMatch(file.Sha256, "^[a-f0-9]{64}$"))
            throw new BridgeException("mobile_attachment_invalid", "Invalid attachment metadata.");
    }
}
