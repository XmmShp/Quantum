using System.Buffers.Binary;
using System.Text.Json;
using Quantum.Mobile.Internal;
using Quantum.Plugins;
using SkiaSharp;

namespace Quantum.Tests;

public sealed class PluginIconAssetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManifestLoadsPngAndDibIcoForBothPluginRuntimes(bool web)
    {
        foreach (var dib in new[] { false, true })
        {
            using var directory = TemporaryDirectory.Create();
            var assets = Path.Combine(directory.Path, "wwwroot");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "icon.ico"), Icon(dib));
            File.WriteAllText(Path.Combine(directory.Path, "Test.dll"), "");
            File.WriteAllText(Path.Combine(assets, "index.js"), "test");
            File.WriteAllText(Path.Combine(directory.Path, "plugin.json"), JsonSerializer.Serialize(new
            {
                id = "test.icons", version = "1.0.0",
                runtime = new { kind = web ? "web" : "dotnet", entry = web ? "index.js" : "Test.dll" },
                ui = new { routes = new[] { new { path = "/icons", component = "Test.Page", view = "index", icon = "X", iconFile = "icon.ico" } } }
            }));
            var route = Assert.Single(new JsonPluginManifestReader().Read(directory.Path).Manifest.Routes);
            Assert.Equal("icon.ico", route.IconFile);
            Assert.Equal("X", route.Icon);
            using var image = SKBitmap.Decode(MobileIconImage.Decode(route.IconImage!));
            Assert.Equal(64, image.Width);
            Assert.Equal(SKColors.Red, image.GetPixel(32, 32));
            Assert.Equal(route.IconImage, route.WithLocalizedTitles(new Dictionary<string, string> { ["en"] = "Icons" }).IconImage);
        }
    }

    [Fact]
    public void MultiFrameIcoChoosesThe64PixelFrame()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "wwwroot"));
        var small = Icon(false); var large = Icon(false, 64, SKColors.Green);
        var combined = new byte[38 + small.Length - 22 + large.Length - 22];
        small.AsSpan(0, 22).CopyTo(combined); combined[4] = 2;
        large.AsSpan(6, 16).CopyTo(combined.AsSpan(22));
        BinaryPrimitives.WriteInt32LittleEndian(combined.AsSpan(18), 38);
        BinaryPrimitives.WriteInt32LittleEndian(combined.AsSpan(34), 38 + small.Length - 22);
        small.AsSpan(22).CopyTo(combined.AsSpan(38));
        large.AsSpan(22).CopyTo(combined.AsSpan(38 + small.Length - 22));
        File.WriteAllBytes(Path.Combine(directory.Path, "wwwroot", "icon.ico"), combined);
        using var image = SKBitmap.Decode(MobileIconImage.Decode(PluginIconAsset.Load(directory.Path, "icon.ico")!));
        Assert.Equal(SKColors.Green, image.GetPixel(32, 32));
    }

    [Theory]
    [InlineData("../icon.ico")]
    [InlineData("/icon.ico")]
    [InlineData("https://host/icon.ico")]
    [InlineData("icons\\icon.ico")]
    [InlineData("icon.png")]
    [InlineData("icon.svg")]
    [InlineData("icon.ico?x")]
    public void RejectsOtherFormatsAndPathsOutsideStaticRoot(string path)
        => Assert.Throws<InvalidDataException>(() => PluginIconAsset.Load(Path.GetTempPath(), path));

    [Fact]
    public void RejectsInvalidHeadersAndOffsetsAndOversizedIcons()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "wwwroot"));
        var file = Path.Combine(directory.Path, "wwwroot", "icon.ico");
        foreach (var bytes in new[] { new byte[0], new byte[10], new byte[1024 * 1024 + 1] })
        {
            File.WriteAllBytes(file, bytes);
            Assert.Throws<InvalidDataException>(() => PluginIconAsset.Load(directory.Path, "icon.ico"));
        }
        var corrupt = Icon(false);
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(18), uint.MaxValue);
        File.WriteAllBytes(file, corrupt);
        Assert.Throws<InvalidDataException>(() => PluginIconAsset.Load(directory.Path, "icon.ico"));
        Assert.Null(PluginIconAsset.Load(directory.Path, null));
    }

    internal static byte[] Icon(bool dib, int size = 16, SKColor? color = null)
    {
        byte[] payload;
        if (dib)
        {
            payload = new byte[40 + size * size * 4 + size * 4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, 40);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), size);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), size * 2);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(14), 32);
            for (var i = 40; i < 40 + size * size * 4; i += 4) { payload[i + 2] = 255; payload[i + 3] = 255; }
        }
        else
        {
            using var image = new SKBitmap(size, size);
            image.Erase(color ?? SKColors.Red);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            payload = png.ToArray();
        }
        var bytes = new byte[22 + payload.Length];
        bytes[2] = 1; bytes[4] = 1; bytes[6] = bytes[7] = (byte)size;
        bytes[10] = 1; bytes[12] = 32;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 22);
        payload.CopyTo(bytes, 22);
        return bytes;
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quantum-icons-" + Guid.NewGuid().ToString("N"));
        public static TemporaryDirectory Create()
        {
            var directory = new TemporaryDirectory();
            Directory.CreateDirectory(directory.Path);
            return directory;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

}
