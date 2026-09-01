using Quantum.Plugin.Abstraction;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginCatalogTests
{
    [Fact]
    public void Environment_ReportsLoadedPluginsAndActiveIntegrations()
    {
        var target = Loaded("target", "1.2.0");
        var owner = Loaded(
            "owner",
            "1.0.0",
            [new PluginIntegration(target.Manifest.Id, SemanticVersion.Parse("1.1.0"))]);
        IQuantumPluginEnvironment environment = new PluginCatalog([owner, target]);

        Assert.True(environment.IsPluginLoaded("TARGET"));
        Assert.True(environment.IsIntegrationActive("owner", "target"));
        Assert.False(environment.IsIntegrationActive("target", "owner"));
        Assert.False(environment.IsIntegrationActive("owner", "missing"));
        Assert.Equal(2, environment.LoadedPlugins.Count);
    }

    [Fact]
    public void Environment_DoesNotActivateVersionIncompatibleIntegration()
    {
        var target = Loaded("target", "1.0.0");
        var owner = Loaded(
            "owner",
            "1.0.0",
            [new PluginIntegration(target.Manifest.Id, SemanticVersion.Parse("2.0.0"))]);
        IQuantumPluginEnvironment environment = new PluginCatalog([owner, target]);

        Assert.False(environment.IsIntegrationActive("owner", "target"));
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
        Assert.True(catalog.IsPluginLoaded("target"));
    }

    private static LoadedPlugin Loaded(
        string id,
        string version,
        IReadOnlyList<PluginIntegration>? integrations = null)
        => new(
            new PluginManifest(
                new PluginId(id),
                SemanticVersion.Parse(version),
                $"{id}.dll",
                integrations: integrations),
            Path.Combine("plugins", id),
            typeof(PluginCatalogTests).Assembly,
            []);
}
