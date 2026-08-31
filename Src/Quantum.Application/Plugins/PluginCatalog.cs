using System.Reflection;
using Microsoft.AspNetCore.Components;
using Quantum.Domain.Plugins;

namespace Quantum.Application.Plugins;

public sealed class PluginCatalog
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
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<PluginRouteRegistration> Routes { get; }

    public IReadOnlyList<PluginLoadFailure> Failures { get; }

    public PluginRouteRegistration? FindRoute(string path)
    {
        var normalized = path.Length > 1 ? path.TrimEnd('/') : path;
        return Routes.FirstOrDefault(route => string.Equals(
            route.Definition.Path,
            normalized,
            StringComparison.OrdinalIgnoreCase));
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
