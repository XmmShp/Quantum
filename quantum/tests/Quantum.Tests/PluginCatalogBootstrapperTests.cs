using System.Runtime.Loader;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginCatalogBootstrapperTests
{
    [Fact]
    public void Bootstrap_LoadsExamplePluginRouteIntoAnIsolatedContext()
    {
        var sourceAssembly = Path.Combine(AppContext.BaseDirectory, "Quantum.ExamplePlugin.dll");
        Assert.True(File.Exists(sourceAssembly), $"Example plugin fixture was not found at '{sourceAssembly}'.");

        var modulesRoot = Path.Combine(Path.GetTempPath(), $"quantum-catalog-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(modulesRoot, "quantum.plugin.example");
        Directory.CreateDirectory(pluginRoot);

        try
        {
            File.Copy(sourceAssembly, Path.Combine(pluginRoot, "Quantum.ExamplePlugin.dll"));
            File.WriteAllText(
                Path.Combine(pluginRoot, "plugin.json"),
                """
                {
                  "id": "quantum.plugin.example",
                  "version": "1.0.0",
                  "entryAssembly": "Quantum.ExamplePlugin.dll",
                  "ui": {
                    "routes": [{
                      "path": "/plugins/example",
                      "component": "Quantum.ExamplePlugin.Pages.Index"
                    }]
                  }
                }
                """);

            var catalog = new PluginCatalogBootstrapper().Bootstrap(modulesRoot);

            var plugin = Assert.Single(catalog.Plugins);
            var route = Assert.Single(catalog.Routes);
            Assert.Same(route, Assert.Single(catalog.NavigationRoutes));
            Assert.Empty(catalog.Failures);
            Assert.Equal("/plugins/example", route.Definition.Path);
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(plugin.EntryAssembly!));
        }
        finally
        {
            Directory.Delete(modulesRoot, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_LoadsDependentExampleAfterRequiredExample()
    {
        var exampleAssembly = Path.Combine(AppContext.BaseDirectory, "Quantum.ExamplePlugin.dll");
        var dependentAssembly = Path.Combine(AppContext.BaseDirectory, "Quantum.ExampleDependentPlugin.dll");
        Assert.True(File.Exists(exampleAssembly), $"Example plugin fixture was not found at '{exampleAssembly}'.");
        Assert.True(
            File.Exists(dependentAssembly),
            $"Dependent example plugin fixture was not found at '{dependentAssembly}'.");

        var modulesRoot = Path.Combine(Path.GetTempPath(), $"quantum-dependent-catalog-{Guid.NewGuid():N}");
        var dependentRoot = Path.Combine(modulesRoot, "01-dependent");
        var exampleRoot = Path.Combine(modulesRoot, "02-example");
        Directory.CreateDirectory(dependentRoot);
        Directory.CreateDirectory(exampleRoot);

        try
        {
            File.Copy(exampleAssembly, Path.Combine(exampleRoot, "Quantum.ExamplePlugin.dll"));
            File.Copy(dependentAssembly, Path.Combine(dependentRoot, "Quantum.ExampleDependentPlugin.dll"));
            File.WriteAllText(
                Path.Combine(exampleRoot, "plugin.json"),
                """
                {
                  "id": "quantum.plugin.example",
                  "version": "0.1.0",
                  "entryAssembly": "Quantum.ExamplePlugin.dll"
                }
                """);
            File.WriteAllText(
                Path.Combine(dependentRoot, "plugin.json"),
                """
                {
                  "id": "quantum.plugin.example-dependent",
                  "version": "0.1.0",
                  "entryAssembly": "Quantum.ExampleDependentPlugin.dll",
                  "dependencies": [{
                    "id": "quantum.plugin.example",
                    "minVersion": "0.1.0"
                  }],
                  "ui": {
                    "routes": [{
                      "path": "/plugins/example-dependent",
                      "component": "Quantum.ExampleDependentPlugin.Pages.Index"
                    }]
                  }
                }
                """);

            var catalog = new PluginCatalogBootstrapper().Bootstrap(modulesRoot);

            Assert.Empty(catalog.Failures);
            Assert.Equal(
                ["quantum.plugin.example", "quantum.plugin.example-dependent"],
                catalog.Plugins.Select(static plugin => (string)plugin.Manifest.Id));
            var dependent = catalog.Plugins[1];
            var dependency = Assert.Single(dependent.Manifest.Dependencies);
            Assert.Equal("quantum.plugin.example", (string)dependency.Id);
            Assert.Equal("0.1.0", dependency.MinimumVersion.ToString());
            Assert.Equal(
                "/plugins/example-dependent",
                Assert.Single(dependent.Routes).Definition.Path);
        }
        finally
        {
            Directory.Delete(modulesRoot, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_LoadsWebPluginWithoutAssemblyLoadContext()
    {
        var modulesRoot = Path.Combine(Path.GetTempPath(), $"quantum-web-catalog-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(modulesRoot, "quantum.plugin.web");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "wwwroot"));

        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "wwwroot", "plugin.js"), "export default {};");
            File.WriteAllText(
                Path.Combine(pluginRoot, "plugin.json"),
                """
                {
                  "id": "quantum.plugin.web",
                  "version": "1.0.0",
                  "runtime": { "kind": "web", "entry": "plugin.js" },
                  "ui": {
                    "routes": [{
                      "path": "/plugins/web",
                      "view": "main"
                    }]
                  }
                }
                """);

            var catalog = new PluginCatalogBootstrapper().Bootstrap(modulesRoot);

            var plugin = Assert.Single(catalog.Plugins);
            var route = Assert.Single(catalog.Routes);
            Assert.Same(route, Assert.Single(catalog.NavigationRoutes));
            Assert.Empty(catalog.Failures);
            Assert.Equal(PluginRuntimeKind.Web, plugin.Manifest.Runtime.Kind);
            Assert.Null(plugin.EntryAssembly);
            Assert.Null(plugin.Services);
            Assert.Null(route.ComponentType);
            Assert.Equal("main", route.Definition.View);
        }
        finally
        {
            Directory.Delete(modulesRoot, recursive: true);
        }
    }
}
