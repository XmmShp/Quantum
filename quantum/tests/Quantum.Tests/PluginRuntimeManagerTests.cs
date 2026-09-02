using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Plugins;

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
        Assert.Equal("quantum.plugin.example", failure.PluginId?.ToString());
        Assert.Contains("failed to start", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_LoadsWebPluginWithoutAssemblyOrServiceProvider()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddWebPlugin();
        await using var manager = fixture.CreateManager();

        await manager.InitializeAsync(fixture.HostServices);

        var plugin = fixture.Catalog.FindPlugin("quantum.plugin.web");
        Assert.NotNull(plugin);
        Assert.Equal(PluginRuntimeKind.Web, plugin.Manifest.Runtime.Kind);
        Assert.Null(plugin.EntryAssembly);
        Assert.Null(plugin.Services);
        var route = Assert.Single(plugin.Routes);
        Assert.Null(route.ComponentType);
        Assert.Equal("main", route.Definition.View);
        Assert.True(File.Exists(Path.Combine(plugin.RootPath, "wwwroot", "dist", "plugin.js")));
    }

    [Fact]
    public async Task WebPluginSqlMigrationsApplyOnceAndUpgradeInOrder()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddMigratingWebPlugin();
        await using var manager = fixture.CreateManager();

        await manager.InitializeAsync(fixture.HostServices);

        Assert.NotNull(fixture.Catalog.FindPlugin("quantum.plugin.web"));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM web_migration_items;"));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM __quantum_plugin_migrations WHERE plugin_id = 'quantum.plugin.web';"));

        var unchangedRefresh = await manager.RefreshAsync();
        Assert.True(unchangedRefresh.Succeeded, unchangedRefresh.Message);
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM web_migration_items;"));

        fixture.WriteWebMigration(
            "002_add_status.sql",
            "ALTER TABLE web_migration_items ADD COLUMN status TEXT NOT NULL DEFAULT 'ready';");
        fixture.WriteWebManifest("1.1.0", usesDatabase: true);
        var upgraded = await manager.RefreshAsync();

        Assert.True(upgraded.Succeeded, upgraded.Message);
        Assert.Equal(
            2L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM __quantum_plugin_migrations WHERE plugin_id = 'quantum.plugin.web';"));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM pragma_table_info('web_migration_items') WHERE name = 'status';"));
    }

    [Fact]
    public async Task RefreshRejectsModifiedAppliedMigrationAndKeepsCurrentRuntime()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddMigratingWebPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var current = fixture.Catalog.FindPlugin("quantum.plugin.web");
        Assert.NotNull(current);

        fixture.WriteWebMigration(
            "001_init.sql",
            "CREATE TABLE web_migration_items (id TEXT PRIMARY KEY, changed TEXT); INSERT INTO web_migration_items (id) VALUES ('changed');");
        fixture.WriteWebManifest("1.1.0", usesDatabase: true);
        var result = await manager.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("modified", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(current.RuntimeId, fixture.Catalog.FindPlugin("quantum.plugin.web")?.RuntimeId);
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM web_migration_items WHERE id = 'initial';"));
    }

    [Fact]
    public async Task FailedMigrationRollsBackTheWholePluginArtifact()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddMigratingWebPlugin();
        fixture.WriteWebMigration("002_invalid.sql", "THIS IS NOT VALID SQL;");
        await using var manager = fixture.CreateManager();

        await manager.InitializeAsync(fixture.HostServices);

        Assert.Null(fixture.Catalog.FindPlugin("quantum.plugin.web"));
        Assert.Equal(
            0L,
            await ExecuteScalarInt64Async(
                fixture.DatabasePath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('web_migration_items', '__quantum_plugin_migrations');"));
    }

    [Fact]
    public async Task InitializeAsync_ExampleServiceSupportsWebHandshakeThroughFqn()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        Assert.True(
            fixture.Catalog.Plugins.Count == 1,
            string.Join(Environment.NewLine, fixture.Catalog.Failures.Select(static failure => failure.ToString())));
        var plugin = fixture.Catalog.Plugins[0];
        var serviceTypeName = "Quantum.ExamplePlugin.IExamplePluginState";
        var service = plugin.Services!.GetService(serviceTypeName);
        Assert.NotNull(service);
        Assert.DoesNotContain(
            service.GetType().GetInterfaces(),
            static type => type == typeof(IQuantumPlugin));
        var contract = Assert.Single(service.GetType().GetInterfaces(), type =>
            string.Equals(type.FullName, serviceTypeName, StringComparison.Ordinal));
        var method = contract.GetMethod("CreateWebHandshakeAsync");
        Assert.NotNull(method);

        var invocation = method.Invoke(service, ["quantum.plugin.example-web", CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;
        var handshake = task.GetType().GetProperty("Result")!.GetValue(task);

        Assert.NotNull(handshake);
        Assert.Equal(1, handshake.GetType().GetProperty("Sequence")!.GetValue(handshake));
        Assert.Contains(
            "quantum.plugin.example-web",
            Assert.IsType<string>(handshake.GetType().GetProperty("Message")!.GetValue(handshake)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_DiscoversStaticBootstrapWithoutRegisteringIt()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();

        await manager.InitializeAsync(fixture.HostServices);

        var plugin = Assert.Single(fixture.Catalog.Plugins);
        var services = plugin.Services!;
        var eventBus = services.GetRequiredService<IQuantumEventBus>();
        var state = services.GetService("Quantum.ExamplePlugin.IExamplePluginState");

        Assert.NotNull(eventBus);
        Assert.Null(services.GetService(typeof(IQuantumPlugin)));
        Assert.NotNull(state);
        Assert.True((bool)state.GetType().GetProperty("IsRunning")!.GetValue(state)!);
    }

    [Fact]
    public async Task UninstallAsync_StopsLifecycleAndDeletesSourceFiles()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var loaded = Assert.Single(fixture.Catalog.Plugins);
        var state = loaded.Services!.GetService("Quantum.ExamplePlugin.IExamplePluginState");
        Assert.NotNull(state);
        Assert.True((bool)state.GetType().GetProperty("IsRunning")!.GetValue(state)!);

        var impact = manager.GetUninstallImpact("quantum.plugin.example");
        var result = await manager.UninstallAsync(
            "quantum.plugin.example",
            impact.CatalogRevision);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(fixture.Catalog.Plugins);
        Assert.False((bool)state.GetType().GetProperty("IsRunning")!.GetValue(state)!);
        Assert.False(Directory.Exists(fixture.PluginRoot));
    }

    [Fact]
    public async Task UninstallAsync_RemovesRuntimeEventBusSubscriptions()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var plugin = Assert.Single(fixture.Catalog.Plugins);
        var pluginBus = plugin.Services!.GetRequiredService<IQuantumEventBus>();
        var calls = 0;
        var subscription = pluginBus.Subscribe(
            QuantumTopic.Of("runtime.status"),
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            });
        await using var publisherBus = new PluginEventBus(
            new QuantumPluginInfo("quantum.test.publisher", "1.0.0"),
            fixture.HostServices.GetRequiredService<PluginEventHub>());
        publisherBus.Resume();
        var publisher = publisherBus.CreatePublisher<RuntimeEvent>(
            QuantumTopic.Of("runtime.status"));
        await publisher.PublishAsync(new RuntimeEvent("before-unload"));

        var impact = manager.GetUninstallImpact("quantum.plugin.example");
        var result = await manager.UninstallAsync(
            "quantum.plugin.example",
            impact.CatalogRevision);
        await publisher.PublishAsync(new RuntimeEvent("after-unload"));
        await subscription.DisposeAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, calls);
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
        var state = rolledBack.Services!.GetService("Quantum.ExamplePlugin.IExamplePluginState");
        Assert.NotNull(state);
        Assert.True((bool)state.GetType().GetProperty("IsRunning")!.GetValue(state)!);
    }

    [Fact]
    public async Task DisableAsync_CascadesThroughStrongDependentsAndCanReenable()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        fixture.AddTransitiveDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var impact = manager.GetDisableImpact("quantum.plugin.example");
        var result = await manager.DisableAsync(
            "quantum.plugin.example",
            impact.CatalogRevision);

        Assert.Equal(
            ["quantum.plugin.transitive", "quantum.plugin.dependent"],
            impact.DependentPluginIds);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("quantum.plugin.dependent", result.Message, StringComparison.Ordinal);
        Assert.Contains("quantum.plugin.transitive", result.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Catalog.Plugins);
        Assert.False(Directory.Exists(fixture.PluginRoot));
        Assert.True(Directory.Exists(Path.Combine(
            fixture.ModulesRoot,
            "disabled",
            "quantum.plugin.example")));
        Assert.True(Directory.Exists(Path.Combine(
            fixture.ModulesRoot,
            "disabled",
            "quantum.plugin.dependent")));
        Assert.True(Directory.Exists(Path.Combine(
            fixture.ModulesRoot,
            "disabled",
            "quantum.plugin.transitive")));
        Assert.Equal(
            ["quantum.plugin.dependent", "quantum.plugin.example", "quantum.plugin.transitive"],
            manager.GetDisabledPlugins().Select(static plugin => plugin.PluginId));

        var incompatibleEnable = await manager.EnableAsync("quantum.plugin.transitive");
        Assert.False(incompatibleEnable.Succeeded);
        Assert.Empty(fixture.Catalog.Plugins);

        var enableBase = await manager.EnableAsync("quantum.plugin.example");
        var enableDependent = await manager.EnableAsync("quantum.plugin.dependent");
        var enableTransitive = await manager.EnableAsync("quantum.plugin.transitive");

        Assert.True(enableBase.Succeeded, enableBase.Message);
        Assert.True(enableDependent.Succeeded, enableDependent.Message);
        Assert.True(enableTransitive.Succeeded, enableTransitive.Message);
        Assert.Equal(3, fixture.Catalog.Plugins.Count);
        Assert.Empty(manager.GetDisabledPlugins());
    }

    [Fact]
    public async Task UninstallAsync_DeletesStrongDependentDirectories()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        fixture.AddTransitiveDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var impact = manager.GetUninstallImpact("quantum.plugin.example");
        var result = await manager.UninstallAsync(
            "quantum.plugin.example",
            impact.CatalogRevision);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(fixture.Catalog.Plugins);
        Assert.False(Directory.Exists(fixture.PluginRoot));
        Assert.False(Directory.Exists(Path.Combine(fixture.ModulesRoot, "quantum.plugin.dependent")));
        Assert.False(Directory.Exists(Path.Combine(fixture.ModulesRoot, "quantum.plugin.transitive")));
        Assert.Empty(Directory.EnumerateDirectories(fixture.ModulesRoot));
    }

    [Fact]
    public async Task UninstallAsync_DeletesDisabledPluginDirectory()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);

        var disableImpact = manager.GetDisableImpact("quantum.plugin.example");
        var disable = await manager.DisableAsync(
            "quantum.plugin.example",
            disableImpact.CatalogRevision);
        Assert.True(disable.Succeeded, disable.Message);
        var disabledPath = Path.Combine(
            fixture.ModulesRoot,
            "disabled",
            "quantum.plugin.example");
        Assert.True(Directory.Exists(disabledPath));

        var uninstallImpact = manager.GetUninstallImpact("quantum.plugin.example");
        var uninstall = await manager.UninstallAsync(
            "quantum.plugin.example",
            uninstallImpact.CatalogRevision);

        Assert.True(uninstall.Succeeded, uninstall.Message);
        Assert.False(Directory.Exists(disabledPath));
        Assert.Empty(manager.GetDisabledPlugins());
    }

    [Fact]
    public async Task InitializeAsync_LogsCriticalPluginLoadingStages()
    {
        using var fixture = new RuntimeFixture();
        var logger = new RecordingLogger();
        await using var manager = fixture.CreateManager(logger);

        await manager.InitializeAsync(fixture.HostServices);

        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Initializing plugin runtime", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Scanning plugin directory", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Discovered plugin quantum.plugin.example", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Copying plugin quantum.plugin.example", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Prepared .NET plugin quantum.plugin.example", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Staged plugin quantum.plugin.example", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Loaded plugin quantum.plugin.example", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, static message =>
            message.StartsWith("Plugin runtime initialization finished", StringComparison.Ordinal));
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
            .ToDictionary(plugin => (string)plugin.Manifest.Id, plugin => plugin.RuntimeId);
        logger.Messages.Clear();

        fixture.WriteManifest("1.1.0");
        var result = await manager.ReloadAsync("quantum.plugin.example");

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("quantum.plugin.dependent", result.Message, StringComparison.Ordinal);
        Assert.Contains("quantum.plugin.transitive", result.Message, StringComparison.Ordinal);
        Assert.Equal("1.1.0", fixture.Catalog.FindPlugin("quantum.plugin.example")!.Manifest.Version.ToString());
        Assert.All(fixture.Catalog.Plugins, plugin =>
            Assert.NotEqual(originalRuntimeIds[(string)plugin.Manifest.Id], plugin.RuntimeId));
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
    public async Task DisableAsync_RejectsStaleCascadeConfirmation()
    {
        using var fixture = new RuntimeFixture();
        fixture.AddDependentPlugin();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var staleImpact = manager.GetDisableImpact("quantum.plugin.example");

        fixture.WriteManifest("1.1.0");
        var reload = await manager.ReloadAsync("quantum.plugin.example");
        Assert.True(reload.Succeeded, reload.Message);
        var result = await manager.DisableAsync(
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
            .ToDictionary(plugin => (string)plugin.Manifest.Id, plugin => plugin.RuntimeId);

        fixture.WriteManifest("1.1.0");
        File.WriteAllText(fixture.DependentManifestPath, "{ invalid json");
        var result = await manager.ReloadAsync("quantum.plugin.example");

        Assert.False(result.Succeeded);
        Assert.Equal(2, fixture.Catalog.Plugins.Count);
        Assert.Equal(
            "1.0.0",
            fixture.Catalog.FindPlugin("quantum.plugin.example")!.Manifest.Version.ToString());
        Assert.All(fixture.Catalog.Plugins, plugin =>
            Assert.Equal(originalRuntimeIds[(string)plugin.Manifest.Id], plugin.RuntimeId));
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

    [Fact]
    public async Task InstallPackage_SelectsNewestDuplicateAndInstallsCompatibleBundle()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        using var archive = CreateArchive(
            new ArchiveFile("example-old/plugin.json", DotNetManifest("quantum.plugin.example", "0.9.0")),
            new ArchiveFile("example-old/Quantum.ExamplePlugin.dll", File.ReadAllBytes(fixture.SourceAssemblyPath)),
            new ArchiveFile("example-new/plugin.json", DotNetManifest("quantum.plugin.example", "1.2.0")),
            new ArchiveFile("example-new/Quantum.ExamplePlugin.dll", File.ReadAllBytes(fixture.SourceAssemblyPath)),
            new ArchiveFile(
                "addon/plugin.json",
                WebManifest(
                    "quantum.plugin.addon",
                    "2.0.0",
                    """
                    ,"dependencies":[{"id":"quantum.plugin.example","minVersion":"1.1.0"}]
                    """)),
            new ArchiveFile("addon/wwwroot/dist/plugin.js", "export default { mount() {} };"));

        var preview = await manager.PrepareInstallAsync(archive, "community-bundle.zip");

        Assert.True(preview.CanInstall, string.Join(Environment.NewLine, preview.Issues));
        Assert.Equal(2, preview.InstallCount);
        Assert.Collection(
            preview.Plugins,
            plugin =>
            {
                Assert.Equal("quantum.plugin.addon", plugin.PluginId);
                Assert.Equal(PluginInstallAction.Install, plugin.Action);
                Assert.Null(plugin.InstalledVersion);
            },
            plugin =>
            {
                Assert.Equal("quantum.plugin.example", plugin.PluginId);
                Assert.Equal("1.2.0", plugin.PackageVersion);
                Assert.Equal("1.0.0", plugin.InstalledVersion);
                Assert.Equal(PluginInstallAction.Upgrade, plugin.Action);
            });

        var result = await manager.InstallAsync(preview.PreviewId!);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("1.2.0", fixture.Catalog.FindPlugin("quantum.plugin.example")?.Manifest.Version.ToString());
        Assert.Equal("2.0.0", fixture.Catalog.FindPlugin("quantum.plugin.addon")?.Manifest.Version.ToString());
        Assert.Equal(
            "1.2.0",
            JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.PluginRoot, "plugin.json")))
                .RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task PrepareInstall_RejectsEntireBundleWhenDependencyIsIncompatible()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        using var archive = CreateArchive(
            new ArchiveFile(
                "broken/plugin.json",
                WebManifest(
                    "quantum.plugin.broken",
                    "1.0.0",
                    """
                    ,"dependencies":[{"id":"quantum.plugin.missing","minVersion":"3.0.0"}]
                    """)),
            new ArchiveFile("broken/wwwroot/dist/plugin.js", "export default { mount() {} };"));

        var preview = await manager.PrepareInstallAsync(archive, "incompatible.zip");

        Assert.False(preview.CanInstall);
        Assert.Null(preview.PreviewId);
        Assert.Contains(preview.Issues, issue =>
            issue.PluginId == "quantum.plugin.broken"
            && issue.Reason.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.Single(fixture.Catalog.Plugins);
        Assert.False(Directory.Exists(Path.Combine(fixture.ModulesRoot, "quantum.plugin.broken")));
    }

    [Fact]
    public async Task PrepareInstall_RejectsPluginThatAlreadyExistsInDisabledDirectory()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var disableImpact = manager.GetDisableImpact("quantum.plugin.example");
        var disable = await manager.DisableAsync(
            "quantum.plugin.example",
            disableImpact.CatalogRevision);
        Assert.True(disable.Succeeded, disable.Message);
        using var archive = CreateArchive(
            new ArchiveFile("example/plugin.json", DotNetManifest("quantum.plugin.example", "1.2.0")),
            new ArchiveFile("example/Quantum.ExamplePlugin.dll", File.ReadAllBytes(fixture.SourceAssemblyPath)));

        var preview = await manager.PrepareInstallAsync(archive, "disabled-upgrade.zip");

        Assert.False(preview.CanInstall);
        Assert.Contains(preview.Issues, issue =>
            issue.PluginId == "quantum.plugin.example"
            && issue.Reason.Contains("已禁用", StringComparison.Ordinal));
        Assert.Single(manager.GetDisabledPlugins());
        Assert.Empty(fixture.Catalog.Plugins);
    }

    [Fact]
    public async Task PrepareInstall_RejectsArchivePathTraversal()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        using var archive = CreateArchive(new ArchiveFile("../escaped.txt", "unsafe"));

        var preview = await manager.PrepareInstallAsync(archive, "unsafe.zip");

        Assert.False(preview.CanInstall);
        Assert.Contains(preview.Issues, issue =>
            issue.Reason.Contains("不安全路径", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(manager.SessionShadowRoot, "escaped.txt")));
    }

    [Fact]
    public async Task InstallPackage_RestoresModulesWhenNewLifecycleFails()
    {
        using var fixture = new RuntimeFixture();
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var originalRuntimeId = Assert.Single(fixture.Catalog.Plugins).RuntimeId;
        using var archive = CreateArchive(
            new ArchiveFile("example/plugin.json", DotNetManifest("quantum.plugin.example", "1.1.0")),
            new ArchiveFile("example/Quantum.ExamplePlugin.dll", File.ReadAllBytes(fixture.SourceAssemblyPath)),
            new ArchiveFile("example/fail-start", string.Empty));
        var preview = await manager.PrepareInstallAsync(archive, "failing-upgrade.zip");
        Assert.True(preview.CanInstall, string.Join(Environment.NewLine, preview.Issues));

        var result = await manager.InstallAsync(preview.PreviewId!);

        Assert.False(result.Succeeded);
        var current = Assert.Single(fixture.Catalog.Plugins);
        Assert.Equal(originalRuntimeId, current.RuntimeId);
        Assert.Equal("1.0.0", current.Manifest.Version.ToString());
        Assert.Equal(
            "1.0.0",
            JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.PluginRoot, "plugin.json")))
                .RootElement.GetProperty("version").GetString());
        Assert.False(File.Exists(Path.Combine(fixture.PluginRoot, "fail-start")));
    }

    private static async Task<WeakReference> LoadReloadAndDisposeAsync(RuntimeFixture fixture)
    {
        await using var manager = fixture.CreateManager();
        await manager.InitializeAsync(fixture.HostServices);
        var original = Assert.Single(fixture.Catalog.Plugins);
        var loadContext = AssemblyLoadContext.GetLoadContext(original.EntryAssembly!);
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

    private static MemoryStream CreateArchive(params ArchiveFile[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var destination = entry.Open();
                destination.Write(file.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string DotNetManifest(string pluginId, string version)
        => $$"""
           {
             "id": "{{pluginId}}",
             "version": "{{version}}",
             "entryAssembly": "Quantum.ExamplePlugin.dll"
           }
           """;

    private static string WebManifest(string pluginId, string version, string extraProperties = "")
        => $$"""
           {
             "id": "{{pluginId}}",
             "version": "{{version}}",
             "runtime": { "kind": "web", "entry": "dist/plugin.js" }
             {{extraProperties}}
           }
           """;

    private static async Task<long> ExecuteScalarInt64Async(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record RuntimeEvent(string State);

    private sealed record ArchiveFile
    {
        public ArchiveFile(string path, string content)
            : this(path, System.Text.Encoding.UTF8.GetBytes(content))
        {
        }

        public ArchiveFile(string path, byte[] content)
        {
            Path = path;
            Content = content;
        }

        public string Path { get; }

        public byte[] Content { get; }
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
                .AddQuantumPluginEventBus()
                .BuildServiceProvider();
        }

        public string ModulesRoot { get; }

        public string PluginRoot { get; }

        public string ShadowRoot { get; }

        public string SourceAssemblyPath { get; }

        public string DatabasePath => Path.Combine(_root, "quantum.db");

        public string DependentManifestPath => Path.Combine(
            ModulesRoot,
            "quantum.plugin.dependent",
            "plugin.json");

        public PluginCatalog Catalog { get; }

        public ServiceProvider HostServices { get; }

        public PluginRuntimeManager CreateManager(ILogger<PluginRuntimeManager>? logger = null)
            => new(
                Catalog,
                new PluginRuntimeOptions(
                    ModulesRoot,
                    ShadowRoot,
                    DatabasePath),
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

        public void AddWebPlugin()
        {
            var webRoot = Path.Combine(ModulesRoot, "quantum.plugin.web");
            Directory.CreateDirectory(Path.Combine(webRoot, "wwwroot", "dist"));
            File.WriteAllText(
                Path.Combine(webRoot, "wwwroot", "dist", "plugin.js"),
                "export default { mount() {} };");
            WriteWebManifest("1.0.0", usesDatabase: false);
        }

        public void AddMigratingWebPlugin()
        {
            AddWebPlugin();
            WriteWebMigration(
                "001_init.sql",
                "CREATE TABLE web_migration_items (id TEXT PRIMARY KEY); INSERT INTO web_migration_items (id) VALUES ('initial');");
            WriteWebManifest("1.0.0", usesDatabase: true);
        }

        public void WriteWebMigration(string fileName, string sql)
        {
            var migrationsRoot = Path.Combine(ModulesRoot, "quantum.plugin.web", "migrations");
            Directory.CreateDirectory(migrationsRoot);
            File.WriteAllText(Path.Combine(migrationsRoot, fileName), sql);
        }

        public void WriteWebManifest(string version, bool usesDatabase)
        {
            var webRoot = Path.Combine(ModulesRoot, "quantum.plugin.web");
            File.WriteAllText(
                Path.Combine(webRoot, "plugin.json"),
                $$"""
                {
                  "id": "quantum.plugin.web",
                  "version": "{{version}}",
                  "runtime": { "kind": "web", "entry": "dist/plugin.js" },
                  {{(usesDatabase ? "\"database\": { \"migrations\": \"./migrations\" }," : string.Empty)}}
                  "ui": {
                    "routes": [{
                      "path": "/plugins/web",
                      "view": "main"
                    }]
                  }
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
