using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Application.Plugins;
using Quantum.Infrastructure.Plugins;
using Quantum.Plugin.Abstraction;

namespace Quantum.Tests;

public sealed class PluginRuntimeManagerTests
{
    [Fact]
    public async Task InitializeAsync_ExcludesPluginWhoseLifecycleFailsToStart()
    {
        using var fixture = new RuntimeFixture();
        File.WriteAllText(Path.Combine(fixture.PluginRoot, "fail-start"), string.Empty);
        await using var manager = fixture.CreateManager();

        await manager.InitializeAsync(fixture.HostServices);

        Assert.Empty(fixture.Catalog.Plugins);
        var failure = Assert.Single(fixture.Catalog.Failures);
        Assert.Equal("quantum.plugin.example", failure.PluginId?.Value);
        Assert.Contains("failed to start", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnloadAsync_StopsLifecycleAndReleasesSourceFiles()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var loaded = Assert.Single(fixture.Catalog.Plugins);
        var lifecycle = Assert.IsAssignableFrom<IQuantumPlugin>(
            loaded.Services!.GetRequiredService<IQuantumPlugin>());
        Assert.True((bool)lifecycle.GetType().GetProperty("IsRunning")!.GetValue(lifecycle)!);

        var result = await manager.UnloadAsync("quantum.plugin.example");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(fixture.Catalog.Plugins);
        Assert.False((bool)lifecycle.GetType().GetProperty("IsRunning")!.GetValue(lifecycle)!);
        File.Copy(
            fixture.SourceAssemblyPath,
            Path.Combine(fixture.PluginRoot, "replacement.dll"),
            overwrite: true);
    }

    [Fact]
    public async Task ReloadAsync_SwitchesVersionAndCollectsPreviousLoadContext()
    {
        using var fixture = new RuntimeFixture();
        var weakReference = await LoadReloadAndDisposeAsync(fixture);

        ForceCollection();

        Assert.False(weakReference.IsAlive);
    }

    [Fact]
    public async Task ReloadAsync_RollsBackWhenNewLifecycleFailsToStart()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var original = Assert.Single(fixture.Catalog.Plugins);

        fixture.WriteManifest("1.1.0");
        File.WriteAllText(Path.Combine(fixture.PluginRoot, "fail-start"), string.Empty);
        var result = await manager.ReloadAsync("quantum.plugin.example");

        Assert.False(result.Succeeded);
        var rolledBack = Assert.Single(fixture.Catalog.Plugins);
        Assert.Equal("1.0.0", rolledBack.Manifest.Version.ToString());
        Assert.Equal(original.RuntimeId, rolledBack.RuntimeId);
        var lifecycle = rolledBack.Services!.GetRequiredService<IQuantumPlugin>();
        Assert.True((bool)lifecycle.GetType().GetProperty("IsRunning")!.GetValue(lifecycle)!);
    }

    [Fact]
    public async Task UnloadAsync_CascadesThroughStrongDependents()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        fixture.AddTransitiveDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var impact = manager.GetUnloadImpact("quantum.plugin.example");
        var unconfirmed = await manager.UnloadAsync("quantum.plugin.example");
        Assert.False(unconfirmed.Succeeded);
        Assert.Contains("quantum.plugin.transitive", unconfirmed.Message, StringComparison.Ordinal);
        Assert.Equal(3, fixture.Catalog.Plugins.Count);

        var result = await manager.UnloadAsync(
            "quantum.plugin.example",
            impact.CatalogRevision);

        Assert.Equal(
            ["quantum.plugin.transitive", "quantum.plugin.dependent"],
            impact.DependentPluginIds);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("quantum.plugin.dependent", result.Message, StringComparison.Ordinal);
        Assert.Contains("quantum.plugin.transitive", result.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Catalog.Plugins);
    }

    [Fact]
    public async Task ReloadAsync_RestartsStrongDependentsAfterDependencyUpdate()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        fixture.AddTransitiveDependentPlugin();
        var logger = new RecordingLogger();
        await using var manager = fixture.CreateManager(logger);
        await manager.InitializeAsync(fixture.HostServices);
        var originalRuntimeIds = fixture.Catalog.Plugins
            .ToDictionary(plugin => plugin.Manifest.Id.Value, plugin => plugin.RuntimeId);
        logger.Messages.Clear();

        fixture.WriteManifest("1.1.0");
        var result = await manager.ReloadAsync("quantum.plugin.example");

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("quantum.plugin.dependent", result.Message, StringComparison.Ordinal);
        Assert.Contains("quantum.plugin.transitive", result.Message, StringComparison.Ordinal);
        Assert.Equal("1.1.0", fixture.Catalog.FindPlugin("quantum.plugin.example")!.Manifest.Version.ToString());
        Assert.All(fixture.Catalog.Plugins, plugin =>
            Assert.NotEqual(originalRuntimeIds[plugin.Manifest.Id.Value], plugin.RuntimeId));
        Assert.Collection(
            logger.Messages.Where(static message => message.StartsWith("Stopped lifecycle", StringComparison.Ordinal)),
            message => Assert.Contains("quantum.plugin.transitive", message, StringComparison.Ordinal),
            message => Assert.Contains("quantum.plugin.dependent", message, StringComparison.Ordinal),
            message => Assert.Contains("quantum.plugin.example", message, StringComparison.Ordinal));
        Assert.Collection(
            logger.Messages.Where(static message => message.StartsWith("Started lifecycle", StringComparison.Ordinal)),
            message => Assert.Contains("quantum.plugin.example", message, StringComparison.Ordinal),
            message => Assert.Contains("quantum.plugin.dependent", message, StringComparison.Ordinal),
            message => Assert.Contains("quantum.plugin.transitive", message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnloadAsync_RejectsStaleCascadeConfirmation()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var staleImpact = manager.GetUnloadImpact("quantum.plugin.example");

        fixture.WriteManifest("1.1.0");
        var reload = await manager.ReloadAsync("quantum.plugin.example");
        Assert.True(reload.Succeeded, reload.Message);
        var result = await manager.UnloadAsync(
            "quantum.plugin.example",
            staleImpact.CatalogRevision);

        Assert.False(result.Succeeded);
        Assert.Contains("最新清单", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Catalog.Plugins.Count);
    }

    [Fact]
    public async Task ReloadAsync_PreservesSnapshotWhenAnotherLoadedPluginBecomesInvalidOnDisk()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var originalRuntimeIds = fixture.Catalog.Plugins
            .ToDictionary(plugin => plugin.Manifest.Id.Value, plugin => plugin.RuntimeId);

        fixture.WriteManifest("1.1.0");
        File.WriteAllText(fixture.DependentManifestPath, "{ invalid json");
        var result = await manager.ReloadAsync("quantum.plugin.example");

        Assert.False(result.Succeeded);
        Assert.Equal(2, fixture.Catalog.Plugins.Count);
        Assert.Equal(
            "1.0.0",
            fixture.Catalog.FindPlugin("quantum.plugin.example")!.Manifest.Version.ToString());
        Assert.All(fixture.Catalog.Plugins, plugin =>
            Assert.Equal(originalRuntimeIds[plugin.Manifest.Id.Value], plugin.RuntimeId));
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMoreThanOnce()
    {
        using var fixture = new RuntimeFixture();
        var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        await manager.DisposeAsync();
        await manager.DisposeAsync();

        Assert.Empty(fixture.Catalog.Plugins);
    }

    private static async Task<WeakReference> LoadReloadAndDisposeAsync(RuntimeFixture fixture)
    {
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var original = Assert.Single(fixture.Catalog.Plugins);
        var loadContext = AssemblyLoadContext.GetLoadContext(original.EntryAssembly);
        Assert.NotNull(loadContext);
        var weakReference = new WeakReference(loadContext, trackResurrection: false);

        fixture.WriteManifest("1.1.0");
        var result = await manager.ReloadAsync("quantum.plugin.example");
        Assert.True(result.Succeeded, result.Message);
        var upgraded = Assert.Single(fixture.Catalog.Plugins);
        Assert.Equal("1.1.0", upgraded.Manifest.Version.ToString());
        Assert.NotEqual(original.RuntimeId, upgraded.RuntimeId);
        return weakReference;
    }

    private static void ForceCollection()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"quantum-runtime-{Guid.NewGuid():N}");

        public RuntimeFixture()
        {
            ModulesRoot = Path.Combine(_root, "Modules");
            PluginRoot = Path.Combine(ModulesRoot, "quantum.plugin.example");
            ShadowRoot = Path.Combine(_root, "Shadow");
            Directory.CreateDirectory(PluginRoot);

            SourceAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Quantum.ExamplePlugin.dll");
            Assert.True(File.Exists(SourceAssemblyPath));
            File.Copy(SourceAssemblyPath, Path.Combine(PluginRoot, "Quantum.ExamplePlugin.dll"));
            WriteManifest("1.0.0");

            Catalog = new PluginCatalog([]);
            HostServices = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                .BuildServiceProvider();
        }

        public string ModulesRoot { get; }

        public string PluginRoot { get; }

        public string ShadowRoot { get; }

        public string SourceAssemblyPath { get; }

        public string DependentManifestPath => Path.Combine(
            ModulesRoot,
            "quantum.plugin.dependent",
            "plugin.json");

        public PluginCatalog Catalog { get; }

        public ServiceProvider HostServices { get; }

        public PluginRuntimeManager CreateManager(ILogger<PluginRuntimeManager>? logger = null)
            => new(
                Catalog,
                new PluginRuntimeOptions(ModulesRoot, ShadowRoot),
                logger: logger ?? NullLogger<PluginRuntimeManager>.Instance);

        public void WriteManifest(string version)
        {
            File.WriteAllText(
                Path.Combine(PluginRoot, "plugin.json"),
                $$"""
                {
                  "id": "quantum.plugin.example",
                  "version": "{{version}}",
                  "entryAssembly": "Quantum.ExamplePlugin.dll",
                  "ui": {
                    "routes": [{
                      "path": "/plugins/example",
                      "component": "Quantum.ExamplePlugin.Pages.Index"
                    }]
                  }
                }
                """);
        }

        public void AddDependentPlugin()
        {
            var dependentRoot = Path.Combine(ModulesRoot, "quantum.plugin.dependent");
            Directory.CreateDirectory(dependentRoot);
            File.Copy(SourceAssemblyPath, Path.Combine(dependentRoot, "Quantum.ExamplePlugin.dll"));
            File.WriteAllText(
                DependentManifestPath,
                """
                {
                  "id": "quantum.plugin.dependent",
                  "version": "1.0.0",
                  "entryAssembly": "Quantum.ExamplePlugin.dll",
                  "dependencies": [{
                    "id": "quantum.plugin.example",
                    "minVersion": "1.0.0"
                  }]
                }
                """);
        }

        public void AddTransitiveDependentPlugin()
        {
            var transitiveRoot = Path.Combine(ModulesRoot, "quantum.plugin.transitive");
            Directory.CreateDirectory(transitiveRoot);
            File.Copy(SourceAssemblyPath, Path.Combine(transitiveRoot, "Quantum.ExamplePlugin.dll"));
            File.WriteAllText(
                Path.Combine(transitiveRoot, "plugin.json"),
                """
                {
                  "id": "quantum.plugin.transitive",
                  "version": "1.0.0",
                  "entryAssembly": "Quantum.ExamplePlugin.dll",
                  "dependencies": [{
                    "id": "quantum.plugin.dependent",
                    "minVersion": "1.0.0"
                  }]
                }
                """);
        }

        public void Dispose()
        {
            HostServices.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class RecordingLogger : ILogger<PluginRuntimeManager>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => Scope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class Scope : IDisposable
        {
            public static Scope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
