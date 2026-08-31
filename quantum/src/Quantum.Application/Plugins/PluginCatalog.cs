using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Quantum.Domain.Plugins;
using Quantum.Plugin.Abstraction;

namespace Quantum.Application.Plugins;

public sealed class PluginCatalog : IQuantumPluginEnvironment
{
    private PluginCatalogSnapshot _snapshot;

    public PluginCatalog(IEnumerable<LoadedPlugin> plugins, IEnumerable<PluginLoadFailure>? failures = null)
    {
        _snapshot = CreateSnapshot(plugins, failures ?? [], revision: 0);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<LoadedPlugin> Plugins => Snapshot.Plugins;

    public IReadOnlyList<PluginRouteRegistration> Routes => Snapshot.Routes;

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
        RaiseChanged();
    }

    public bool IsPluginLoaded(string pluginId)
        => FindPlugin(pluginId) is not null;

    public bool IsIntegrationActive(string ownerPluginId, string targetPluginId)
    {
        var owner = FindPlugin(ownerPluginId);
        var target = FindPlugin(targetPluginId);
        if (owner is null || target is null)
        {
            return false;
        }

        var integration = owner.Manifest.Integrations.FirstOrDefault(candidate =>
            candidate.Id == target.Manifest.Id);
        return integration is not null
            && target.Manifest.Version.CompareTo(integration.MinimumVersion) >= 0;
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
            plugin.Manifest.Id.Value,
            normalizedId,
            StringComparison.Ordinal));
    }

    public LoadedPlugin? FindPlugin(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Plugins.FirstOrDefault(plugin => ReferenceEquals(plugin.EntryAssembly, assembly));
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
                Trace.TraceError($"Plugin catalog change observer failed: {exception}");
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
        var loadedPlugins = pluginArray
            .Select(static plugin => new QuantumPluginInfo(
                plugin.Manifest.Id.Value,
                plugin.Manifest.Version.ToString()))
            .ToArray();
        return new PluginCatalogSnapshot(
            pluginArray,
            routes,
            failures.ToArray(),
            loadedPlugins,
            revision);
    }
}

internal sealed record PluginCatalogSnapshot(
    IReadOnlyList<LoadedPlugin> Plugins,
    IReadOnlyList<PluginRouteRegistration> Routes,
    IReadOnlyList<PluginLoadFailure> Failures,
    IReadOnlyList<QuantumPluginInfo> LoadedPlugins,
    long Revision);

public sealed class LoadedPlugin
{
    public LoadedPlugin(
        PluginManifest manifest,
        string rootPath,
        Assembly entryAssembly,
        IReadOnlyList<PluginRouteRegistration> routes,
        Guid runtimeId = default,
        IServiceProvider? services = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
        EntryAssembly = entryAssembly ?? throw new ArgumentNullException(nameof(entryAssembly));
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        RuntimeId = runtimeId;
        Services = services;
    }

    public PluginManifest Manifest { get; }

    public string RootPath { get; }

    public Assembly EntryAssembly { get; }

    public IReadOnlyList<PluginRouteRegistration> Routes { get; }

    public Guid RuntimeId { get; }

    public IServiceProvider? Services { get; }
}

public sealed record PluginRouteRegistration(
    PluginId PluginId,
    PluginRouteDefinition Definition,
    Type ComponentType)
{
    public static PluginRouteRegistration Create(
        PluginId pluginId,
        PluginRouteDefinition definition,
        Assembly assembly)
    {
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
}
