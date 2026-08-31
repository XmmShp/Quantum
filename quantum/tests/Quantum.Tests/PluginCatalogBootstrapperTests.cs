using System.Runtime.Loader;
using Quantum.Infrastructure.Plugins;

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
            Assert.Empty(catalog.Failures);
            Assert.Equal("/plugins/example", route.Definition.Path);
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(plugin.EntryAssembly));
        }
        finally
        {
            Directory.Delete(modulesRoot, recursive: true);
        }
    }
}
