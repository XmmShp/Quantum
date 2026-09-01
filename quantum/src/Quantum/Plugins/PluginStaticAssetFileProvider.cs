using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
namespace Quantum.Plugins;

public sealed class PluginStaticAssetFileProvider : IFileProvider, IDisposable
{
    private readonly PluginCatalog _catalog;
    private IReadOnlyDictionary<string, PhysicalFileProvider> _providers;
    private bool _disposed;

    public PluginStaticAssetFileProvider(PluginCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _providers = CreateProviders(catalog);
        _catalog.Changed += OnCatalogChanged;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalog.Changed -= OnCatalogChanged;
        DisposeProviders(Interlocked.Exchange(
            ref _providers,
            new Dictionary<string, PhysicalFileProvider>()));
    }

    private static IReadOnlyDictionary<string, PhysicalFileProvider> CreateProviders(PluginCatalog catalog)
        => catalog.Plugins
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

    private static void DisposeProviders(IReadOnlyDictionary<string, PhysicalFileProvider> providers)
    {
        foreach (var provider in providers.Values)
        {
            provider.Dispose();
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        var next = CreateProviders(_catalog);
        var previous = Interlocked.Exchange(ref _providers, next);
        DisposeProviders(previous);
    }

    private bool TryResolve(
        string subpath,
        out PhysicalFileProvider provider,
        out string pluginSubpath)
    {
        var parts = subpath.TrimStart('/').Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2
            && string.Equals(parts[0], "_content", StringComparison.OrdinalIgnoreCase)
            && Volatile.Read(ref _providers).TryGetValue(parts[1], out provider!))
        {
            pluginSubpath = parts.Length == 3 ? parts[2] : string.Empty;
            return true;
        }

        provider = null!;
        pluginSubpath = string.Empty;
        return false;
    }
}
