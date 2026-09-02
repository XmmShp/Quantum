using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quantum.Plugins;

public sealed class PluginCatalog : IQuantumPluginEnvironment
{
    private readonly ILogger _logger;
    private PluginCatalogSnapshot _snapshot;

    public PluginCatalog(
        IEnumerable<LoadedPlugin> plugins,
        IEnumerable<PluginLoadFailure>? failures = null,
        ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _snapshot = CreateSnapshot(plugins, failures ?? [], revision: 0);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<LoadedPlugin> Plugins => Snapshot.Plugins;

    public IReadOnlyList<PluginRouteRegistration> Routes => Snapshot.Routes;

    public IReadOnlyList<PluginRouteRegistration> NavigationRoutes => Snapshot.NavigationRoutes;

    public IReadOnlyList<PluginLoadFailure> Failures => Snapshot.Failures;

    public IReadOnlyList<QuantumPluginInfo> LoadedPlugins => Snapshot.LoadedPlugins;

    public long Revision => Snapshot.Revision;

    private PluginCatalogSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Replace(
        IEnumerable<LoadedPlugin> plugins,
        IEnumerable<PluginLoadFailure>? failures = null)
    {
        var next = CreateSnapshot(plugins, failures ?? [], checked(Revision + 1));
        Volatile.Write(ref _snapshot, next);
        _logger.LogInformation(
            "Published plugin catalog revision {CatalogRevision} with {PluginCount} plugin(s), "
            + "{RouteCount} route(s), {NavigationRouteCount} navigation route(s), "
            + "and {FailureCount} failure(s).",
            next.Revision,
            next.Plugins.Count,
            next.Routes.Count,
            next.NavigationRoutes.Count,
            next.Failures.Count);
        RaiseChanged();
    }

    public bool IsPluginLoaded(PluginId pluginId)
    {
        return FindPlugin((string)pluginId) is not null;
    }

    public PluginRouteRegistration? FindRoute(string path)
    {
        var normalized = path.Length > 1 ? path.TrimEnd('/') : path;
        return Routes.FirstOrDefault(route => string.Equals(
            route.Definition.Path,
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }

    public LoadedPlugin? FindPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalizedId = pluginId.Trim().ToLowerInvariant();
        return Plugins.FirstOrDefault(plugin => string.Equals(
            (string)plugin.Manifest.Id,
            normalizedId,
            StringComparison.Ordinal));
    }

    public LoadedPlugin? FindPlugin(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Plugins.FirstOrDefault(plugin => plugin.EntryAssembly is not null
            && ReferenceEquals(plugin.EntryAssembly, assembly));
    }

    private void RaiseChanged()
    {
        foreach (EventHandler handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                // A UI or file-provider observer must not invalidate an already published,
                // internally consistent runtime snapshot or prevent later observers running.
                _logger.LogError(exception, "Plugin catalog change observer failed.");
            }
        }
    }

    private static PluginCatalogSnapshot CreateSnapshot(
        IEnumerable<LoadedPlugin> plugins,
        IEnumerable<PluginLoadFailure> failures,
        long revision)
    {
        var pluginArray = plugins.ToArray();
        var routes = pluginArray
            .SelectMany(static plugin => plugin.Routes)
            .OrderBy(static route => route.Definition.Order)
            .ThenBy(static route => route.Definition.Path, StringComparer.Ordinal)
            .ToArray();
        var navigationRoutes = routes
            .Where(static route => route.Definition.ShowInNavigation)
            .ToArray();
        var loadedPlugins = pluginArray
            .Select(static plugin => new QuantumPluginInfo(
                plugin.Manifest.Id,
                plugin.Manifest.Version))
            .ToArray();
        return new PluginCatalogSnapshot(
            pluginArray,
            routes,
            navigationRoutes,
            failures.ToArray(),
            loadedPlugins,
            revision);
    }
}

internal sealed record PluginCatalogSnapshot(
    IReadOnlyList<LoadedPlugin> Plugins,
    IReadOnlyList<PluginRouteRegistration> Routes,
    IReadOnlyList<PluginRouteRegistration> NavigationRoutes,
    IReadOnlyList<PluginLoadFailure> Failures,
    IReadOnlyList<QuantumPluginInfo> LoadedPlugins,
    long Revision);

public sealed class LoadedPlugin
{
    public LoadedPlugin(
        PluginManifest manifest,
        string rootPath,
        Assembly? entryAssembly,
        IReadOnlyList<PluginRouteRegistration> routes,
        Guid runtimeId = default,
        IServiceProvider? services = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (manifest.Runtime.Kind == PluginRuntimeKind.DotNet && entryAssembly is null)
        {
            throw new ArgumentException("A .NET plugin must provide its entry assembly.", nameof(entryAssembly));
        }

        if (manifest.Runtime.Kind == PluginRuntimeKind.Web && entryAssembly is not null)
        {
            throw new ArgumentException("A Web plugin cannot provide a .NET entry assembly.", nameof(entryAssembly));
        }

        RootPath = rootPath;
        EntryAssembly = entryAssembly;
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        RuntimeId = runtimeId;
        Services = services;
    }

    public PluginManifest Manifest { get; }

    public string RootPath { get; }

    public Assembly? EntryAssembly { get; }

    public IReadOnlyList<PluginRouteRegistration> Routes { get; }

    public Guid RuntimeId { get; }

    public IServiceProvider? Services { get; }

    internal PluginRpcRuntime? RpcRuntime { get; init; }
}

public sealed record PluginRouteRegistration(
    PluginId PluginId,
    PluginRouteDefinition Definition,
    Type? ComponentType)
{
    public static PluginRouteRegistration Create(
        PluginId pluginId,
        PluginRouteDefinition definition,
        Assembly assembly)
    {
        if (definition.Component is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' route '{definition.Path}' does not declare a .NET component.");
        }

        var componentType = assembly.GetType(definition.Component, throwOnError: false, ignoreCase: false)
            ?? throw new InvalidOperationException(
                $"Plugin '{pluginId}' route '{definition.Path}' references missing component '{definition.Component}'.");

        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' route '{definition.Path}' type '{definition.Component}' is not a Blazor component.");
        }

        return new PluginRouteRegistration(pluginId, definition, componentType);
    }

    public static PluginRouteRegistration CreateWeb(
        PluginId pluginId,
        PluginRouteDefinition definition)
    {
        if (definition.View is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' route '{definition.Path}' does not declare a Web view.");
        }

        return new PluginRouteRegistration(pluginId, definition, ComponentType: null);
    }
}
