using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Resources;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Quantum.Platform;

// Reads IL and metadata only. Never loads plugin assemblies into the CLR.
public static class ManagedAssemblyReview
{
    public static string Inspect(string name, byte[] bytes, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) throw new BadImageFormatException("Native binaries require manual inspection.");
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly || pe.PEHeaders.CorHeader is not { } cor ||
            !cor.Flags.HasFlag(CorFlags.ILOnly) || cor.ManagedNativeHeaderDirectory.Size != 0)
            throw new BadImageFormatException("Mixed or native code requires manual inspection.");
        using var module = new PEFile(name, new MemoryStream(bytes, writable: false));
        var resolver = new UniversalAssemblyResolver(null, false, ".NETCoreApp,Version=v10.0");
        // Resolve only from trusted Host/framework locations, never a plugin path or network.
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic))
            if (!string.IsNullOrEmpty(assembly.Location)) resolver.AddSearchDirectory(Path.GetDirectoryName(assembly.Location)!);
        var decompiler = new CSharpDecompiler(module, resolver,
            new DecompilerSettings { ThrowOnAssemblyResolveErrors = false })
        {
            CancellationToken = cancellationToken
        };
        var text = decompiler.DecompileWholeModuleAsString();
        // An unresolved method body is not review evidence.
        if (text.Contains("Error decompiling", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An assembly method could not be decompiled.");
        var resources = new List<object>();
        foreach (var handle in metadata.ManifestResources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = metadata.GetManifestResource(handle);
            if (!resource.Implementation.IsNil) throw new InvalidDataException("External assembly resources require manual inspection.");
            var resourceName = metadata.GetString(resource.Name);
            if (!resourceName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported embedded resources require manual inspection.");
            var data = pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress).GetContent();
            using var resourceStream = new MemoryStream(data.ToArray(), writable: false);
            resourceStream.Position = checked((long)resource.Offset);
            using var reader = new BinaryReader(resourceStream, Encoding.UTF8, leaveOpen: true);
            var length = reader.ReadInt32();
            if (length < 0 || length > bytes.Length) throw new InvalidDataException("Invalid embedded resource length.");
            using var raw = new MemoryStream(reader.ReadBytes(length), writable: false);
            using var resourceReader = new ResourceReader(raw);
            var values = resourceReader.GetEnumerator();
            while (values.MoveNext())
            {
                // Access only keys/raw bytes, never ResourceReader.Value or a serializer.
                var resourceKey = (string)values.Key;
                resourceReader.GetResourceData(resourceKey, out var type, out var value);
                if (type != "ResourceTypeCode.String") throw new InvalidDataException("Non-string resources require manual inspection.");
                using var valueReader = new BinaryReader(new MemoryStream(value, writable: false));
                resources.Add(new { resource = resourceName, key = resourceKey, value = valueReader.ReadString() });
            }
        }
        return $"// Platform-generated static inspection of {name}\n// SHA-256: {Convert.ToHexStringLower(SHA256.HashData(bytes))}\n" + text +
            "\n/* Embedded string resources (read as raw data, never deserialized):\n" + JsonSerializer.Serialize(resources) + "\n*/\n";
    }
}
