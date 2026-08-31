using System.Runtime.Loader;
using Quantum.Infrastructure.Plugins;

namespace Quantum.Tests;

public sealed class PluginLoadContextTests
{
    [Fact]
    public void Constructor_CreatesCollectibleIsolatedContext()
    {
        var assemblyPath = typeof(PluginLoadContextTests).Assembly.Location;
        var firstContext = new PluginLoadContext(assemblyPath);
        var secondContext = new PluginLoadContext(assemblyPath);

        var firstAssembly = firstContext.LoadFromAssemblyPath(assemblyPath);
        var secondAssembly = secondContext.LoadFromAssemblyPath(assemblyPath);

        Assert.True(firstContext.IsCollectible);
        Assert.NotSame(firstContext, secondContext);
        Assert.NotSame(firstAssembly, secondAssembly);
        Assert.Same(firstContext, AssemblyLoadContext.GetLoadContext(firstAssembly));
        Assert.Same(secondContext, AssemblyLoadContext.GetLoadContext(secondAssembly));

        firstContext.Unload();
        secondContext.Unload();
    }
}
