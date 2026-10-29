namespace Quantum.Mobile.Internal;

public static class MobileIconImage
{
    public const int MaximumBytes = 32 * 1024;
    public const int MaximumCatalogCharacters = 512 * 1024;

    public static string Encode(byte[] bytes)
    {
        var type = ContentType(bytes);
        return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
    }

    public static byte[] Decode(string image)
    {
        if (image.Length > ((MaximumBytes + 2) / 3) * 4 + 32)
            throw new ArgumentException("Plugin icon image exceeds the size limit.");
        var separator = image.IndexOf(";base64,", StringComparison.Ordinal);
        if (separator < 0 || !image.StartsWith("data:image/", StringComparison.Ordinal))
            throw new ArgumentException("Invalid plugin icon image.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(image[(separator + 8)..]); }
        catch (FormatException) { throw new ArgumentException("Invalid plugin icon image encoding."); }
        if (image[..separator] != "data:" + ContentType(bytes))
            throw new ArgumentException("Plugin icon image type does not match its content.");
        return bytes;
    }

    private static string ContentType(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes)
            throw new ArgumentException("Plugin icon image exceeds the size limit.");
        var data = bytes.AsSpan();
        if (data.Length >= 24 && data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[16..]) is >= 1 and <= 64 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[20..]) is >= 1 and <= 64) return "image/png";
        throw new ArgumentException("Plugin icon images must be PNG thumbnails no larger than 64px.");
    }
}
