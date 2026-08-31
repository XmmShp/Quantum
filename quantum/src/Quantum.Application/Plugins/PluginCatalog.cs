using System.Reflection;
using Microsoft.AspNetCore.Components;
using Quantum.Domain.Plugins;
using Quantum.Plugin.Abstraction;

namespace Quantum.Application.Plugins;

public sealed class PluginCatalog : IQuantumPluginEnvironment
{
    public PluginCatalog(IEnumerable<LoadedPlugin> plugins, IEnumerable<PluginLoadFailure>? failures = null)
    {
        Plugins = plugins.ToArray();
        Failures = (failures ?? []).ToArray();
        Routes = Plugins
            .SelectMany(static plugin => plugin.Routes)
            .OrderBy(static route => route.Definition.Order)
            .ThenBy(static route => route.Definition.Path, StringComparer.Ordinal)
            .ToArray();
        LoadedPlugins = Plugins
            .Select(static plugin => new QuantumPluginInfo(
                plugin.Manifest.Id.Value,
                plugin.Manifest.Version.ToString()))
            .ToArray();
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<PluginRouteRegistration> Routes { get; }

    public IReadOnlyList<PluginLoadFailure> Failures { get; }

    public IReadOnlyList<QuantumPluginInfo> LoadedPlugins { get; }

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

    private LoadedPlugin? FindPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalizedId = pluginId.Trim().ToLowerInvariant();
        return Plugins.FirstOrDefault(plugin => string.Equals(
            plugin.Manifest.Id.Value,
            normalizedId,
            StringComparison.Ordinal));
    }
}

public sealed record LoadedPlugin(
    PluginManifest Manifest,
    string RootPath,
    Assembly EntryAssembly,
    IReadOnlyList<PluginRouteRegistration> Routes);

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
