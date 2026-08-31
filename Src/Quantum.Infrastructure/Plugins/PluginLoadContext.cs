using System.Reflection;
using System.Runtime.Loader;

namespace Quantum.Infrastructure.Plugins;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;
    private readonly AssemblyDependencyResolver? _resolver;

    public PluginLoadContext(string entryAssemblyPath)
        : base($"Quantum.Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
    {
        _pluginDirectory = Path.GetDirectoryName(entryAssemblyPath)
            ?? throw new ArgumentException("Plugin entry assembly must have a parent directory.", nameof(entryAssemblyPath));

        try
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        }
        catch (PlatformNotSupportedException)
        {
            // Apple mobile-derived runtimes, including Mac Catalyst, do not expose
            // AssemblyDependencyResolver. The plugin directory remains a deterministic
            // fallback because plugin packages are deployed as a flat dependency closure.
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (MustShareWithHost(assemblyName.Name))
        {
            return LoadFromDefaultContext(assemblyName);
        }

        var assemblyPath = _resolver?.ResolveAssemblyToPath(assemblyName)
            ?? ResolveManagedAssemblyFromPluginDirectory(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName)
            ?? ResolveNativeLibraryFromPluginDirectory(unmanagedDllName);
        return libraryPath is null ? nint.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }

    private string? ResolveManagedAssemblyFromPluginDirectory(AssemblyName assemblyName)
    {
        var candidatePath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        return File.Exists(candidatePath) ? candidatePath : null;
    }

    private string? ResolveNativeLibraryFromPluginDirectory(string unmanagedDllName)
    {
        string[] candidateNames =
        [
            unmanagedDllName,
            $"{unmanagedDllName}.dylib",
            $"lib{unmanagedDllName}.dylib",
            $"{unmanagedDllName}.so",
            $"lib{unmanagedDllName}.so",
            $"{unmanagedDllName}.dll"
        ];

        return candidateNames
            .Select(candidateName => Path.Combine(_pluginDirectory, candidateName))
            .FirstOrDefault(File.Exists);
    }

    private static bool MustShareWithHost(string? assemblyName)
        => assemblyName is not null
            && (assemblyName.Equals("Quantum.Plugin.Abstractions", StringComparison.Ordinal)
                || assemblyName.Equals("Quantum.Contract", StringComparison.Ordinal)
                || assemblyName.StartsWith("NOF.", StringComparison.Ordinal)
                || assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal)
                || assemblyName.StartsWith("System.", StringComparison.Ordinal)
                || assemblyName.Equals("System", StringComparison.Ordinal)
                || assemblyName.Equals("netstandard", StringComparison.Ordinal));

    private static Assembly? LoadFromDefaultContext(AssemblyName assemblyName)
    {
        var loadedAssembly = Default.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
        if (loadedAssembly is not null)
        {
            return loadedAssembly;
        }

        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
