using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Quantum.Application.Plugins;

namespace Quantum.Infrastructure.Plugins;

public sealed class PluginStaticAssetFileProvider : IFileProvider, IDisposable
{
    private readonly IReadOnlyDictionary<string, PhysicalFileProvider> _providers;

    public PluginStaticAssetFileProvider(PluginCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _providers = catalog.Plugins
            .Select(plugin => new
            {
                PluginId = plugin.Manifest.Id.Value,
                StaticRoot = Path.Combine(plugin.RootPath, "wwwroot")
            })
            .Where(static plugin => Directory.Exists(plugin.StaticRoot))
            .ToDictionary(
                static plugin => plugin.PluginId,
                static plugin => new PhysicalFileProvider(plugin.StaticRoot),
                StringComparer.OrdinalIgnoreCase);
    }

    public IFileInfo GetFileInfo(string subpath)
        => TryResolve(subpath, out var provider, out var pluginSubpath)
            ? provider.GetFileInfo(pluginSubpath)
            : new NotFoundFileInfo(Path.GetFileName(subpath));

    public IDirectoryContents GetDirectoryContents(string subpath)
        => TryResolve(subpath, out var provider, out var pluginSubpath)
            ? provider.GetDirectoryContents(pluginSubpath)
            : NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter)
        => TryResolve(filter, out var provider, out var pluginSubpath)
            ? provider.Watch(pluginSubpath)
            : NullChangeToken.Singleton;

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
        {
            provider.Dispose();
        }
    }

    private bool TryResolve(
        string subpath,
        out PhysicalFileProvider provider,
        out string pluginSubpath)
    {
        var parts = subpath.TrimStart('/').Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2
            && string.Equals(parts[0], "_content", StringComparison.OrdinalIgnoreCase)
            && _providers.TryGetValue(parts[1], out provider!))
        {
            pluginSubpath = parts.Length == 3 ? parts[2] : string.Empty;
            return true;
        }

        provider = null!;
        pluginSubpath = string.Empty;
        return false;
    }
}
