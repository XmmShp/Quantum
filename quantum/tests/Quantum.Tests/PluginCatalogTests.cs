using Quantum.Application.Plugins;
using Quantum.Domain.Plugins;
using Quantum.Plugin.Abstraction;

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
