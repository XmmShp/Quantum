using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginCatalogTests
{
    [Fact]
    public void Environment_ReportsLoadedPlugins()
    {
        var target = Loaded("target", "1.2.0");
        var owner = Loaded("owner", "1.0.0");
        IQuantumPluginEnvironment environment = new PluginCatalog([owner, target]);

        Assert.True(environment.IsPluginLoaded(PluginId.Of("TARGET")));
        Assert.Equal(2, environment.LoadedPlugins.Count);
    }

    [Fact]
    public void Replace_PublishesSnapshotAndIsolatesObservers()
    {
        var catalog = new PluginCatalog([]);
        var observerCalls = 0;
        catalog.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        catalog.Changed += (_, _) => observerCalls++;

        catalog.Replace([Loaded("target", "1.0.0")]);

        Assert.Equal(1, observerCalls);
        Assert.Equal(1, catalog.Revision);
        Assert.True(catalog.IsPluginLoaded(PluginId.Of("target")));
    }

    [Fact]
    public void NavigationRoutes_ExcludeHiddenRoutesWithoutDisablingRouting()
    {
        var pluginId = PluginId.Of("navigation-test");
        var visibleDefinition = new PluginRouteDefinition(
            "/plugins/navigation-test",
            "Test.Pages.Index",
            order: 20);
        var hiddenDefinition = new PluginRouteDefinition(
            "/plugins/navigation-test/detail",
            "Test.Pages.Detail",
            title: null,
            icon: null,
            order: 10,
            showInNavigation: false);
        var visibleRoute = new PluginRouteRegistration(
            pluginId,
            visibleDefinition,
            typeof(PluginCatalogTests));
        var hiddenRoute = new PluginRouteRegistration(
            pluginId,
            hiddenDefinition,
            typeof(PluginCatalogTests));
        var manifest = new PluginManifest(
            pluginId,
            SemanticVersion.Of("1.0.0"),
            "navigation-test.dll",
            routes: [visibleDefinition, hiddenDefinition]);
        var plugin = new LoadedPlugin(
            manifest,
            Path.Combine("plugins", (string)pluginId),
            typeof(PluginCatalogTests).Assembly,
            [visibleRoute, hiddenRoute]);

        var catalog = new PluginCatalog([plugin]);

        Assert.Equal([hiddenRoute, visibleRoute], catalog.Routes);
        Assert.Equal([visibleRoute], catalog.NavigationRoutes);
        Assert.Same(hiddenRoute, catalog.FindRoute("/plugins/navigation-test/detail"));
    }

    private static LoadedPlugin Loaded(
        string id,
        string version)
        => new(
            new PluginManifest(
                PluginId.Of(id),
                SemanticVersion.Of(version),
                $"{id}.dll"),
            Path.Combine("plugins", id),
            typeof(PluginCatalogTests).Assembly,
            []);
}
