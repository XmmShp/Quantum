using Quantum.Mobile.Internal;
using System.Buffers.Binary;
using SkiaSharp;

namespace Quantum.Plugins;

public static class PluginIconAsset
{
    public static string? Load(string pluginRoot, string? relativePath)
    {
        if (relativePath is null) return null;
        var parts = relativePath.Split('/');
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 240 ||
            parts.Any(p => p is "" or "." or ".." || p.Any(c => char.IsControl(c) || "\\:%?#<>\"|*".Contains(c))))
            throw new InvalidDataException("iconFile must be a relative file path inside the plugin wwwroot directory.");
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        if (extension != ".ico")
            throw new InvalidDataException("iconFile must reference a standard ICO file.");
        var path = Path.Combine(Path.GetFullPath(pluginRoot), "wwwroot");
        foreach (var part in new[] { "" }.Concat(parts))
        {
            path = Path.Combine(path, part);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("iconFile cannot follow a symbolic link.");
        }
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidDataException("iconFile must be at most 1 MiB.");
        try { return ConvertIcon(File.ReadAllBytes(path)); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid iconFile image.", exception); }
    }
    private static string ConvertIcon(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.Length < 22 || BinaryPrimitives.ReadUInt32LittleEndian(span) != 0x00010000)
            throw new ArgumentException("Invalid ICO header.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        if (count == 0 || count > 256 || 6 + count * 16 > span.Length)
            throw new ArgumentException("Invalid ICO directory.");
        // Prefer a frame at least 64px, retaining the best available resolution below that.
        var entries = Enumerable.Range(0, count).Select(i => 6 + i * 16)
            .OrderBy(i => bytes[i] == 0 || bytes[i] >= 64 ? (bytes[i] == 0 ? 256 : bytes[i]) : 1024 - bytes[i]);
        foreach (var entry in entries)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(span[(entry + 8)..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(entry + 12)..]);
            if (size == 0 || offset < 6 + count * 16 || offset > bytes.Length || size > bytes.Length - offset)
                throw new ArgumentException("Invalid ICO image bounds.");
        }
        var chosen = entries.First();
        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[(chosen + 8)..]));
        var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[(chosen + 12)..]));
        var single = new byte[22 + length];
        span[..6].CopyTo(single); single[4] = 1; single[5] = 0;
        span.Slice(chosen, 16).CopyTo(single.AsSpan(6));
        BinaryPrimitives.WriteUInt32LittleEndian(single.AsSpan(18), 22);
        span.Slice(start, length).CopyTo(single.AsSpan(22));
        using var codec = SKCodec.Create(new MemoryStream(single));
        if (codec is null || codec.Info.Width is < 1 or > 256 || codec.Info.Height is < 1 or > 256)
            throw new ArgumentException("ICO frames must be at most 256px.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new ArgumentException("Invalid ICO image.");
        using var image = SKImage.FromBitmap(bitmap);
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        surface.Canvas.Clear(SKColors.Transparent);
        var scale = Math.Min(64f / image.Width, 64f / image.Height);
        var width = image.Width * scale; var height = image.Height * scale;
        surface.Canvas.DrawImage(image, SKRect.Create((64 - width) / 2, (64 - height) / 2, width, height));
        using var thumbnail = surface.Snapshot();
        using var png = thumbnail.Encode(SKEncodedImageFormat.Png, 100);
        return MobileIconImage.Encode(png.ToArray());
    }
}
