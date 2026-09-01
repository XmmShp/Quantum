using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using Quantum.ExampleCalendarPlugin.Application;
using Quantum.ExampleCalendarPlugin.Hosting;
using Quantum.Plugins;
using Quantum.Plugins.Persistence;

namespace Quantum.Tests;

public sealed class CalendarPluginPersistenceTests
{
    [Fact]
    public async Task CalendarPluginPersistenceDoesNotPreventLoadContextCollection()
    {
        var fixture = new DatabaseFixture();
        try
        {
            var weakReference = await LoadAndUnloadCalendarPluginAsync(fixture);

            ForceCollection();

            Assert.False(weakReference.IsAlive);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CalendarItemsSurviveServiceProviderRestartAndSupportCrud()
    {
        var fixture = new DatabaseFixture();
        try
        {
            Guid itemId;
            await using (var provider = BuildCalendarProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var calendar = scope.ServiceProvider.GetRequiredService<ICalendarItemApplicationService>();
                var created = await calendar.CreateAsync(new CreateCalendarItemRequest(
                    "持久化演示",
                    "关闭容器后仍然存在",
                    new DateOnly(2026, 9, 8),
                    new TimeOnly(10, 30),
                    "event-violet"));
                itemId = created.Id;
            }

            await using (var provider = BuildCalendarProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var calendar = scope.ServiceProvider.GetRequiredService<ICalendarItemApplicationService>();
                var persisted = await calendar.GetAsync(itemId);
                Assert.NotNull(persisted);
                Assert.Equal("持久化演示", persisted.Title);

                var updated = await calendar.UpdateAsync(itemId, new UpdateCalendarItemRequest(
                    "已更新事项",
                    "更新也会落盘",
                    new DateOnly(2026, 9, 9),
                    new TimeOnly(14, 0),
                    "event-green"));
                Assert.Equal(new DateOnly(2026, 9, 9), updated.Date);

                var items = await calendar.ListAsync(
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2026, 9, 30));
                var item = Assert.Single(items);
                Assert.Equal("已更新事项", item.Title);

                await calendar.DeleteAsync(itemId);
                Assert.Null(await calendar.GetAsync(itemId));
            }
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DifferentPluginContributorsAddTablesToTheSameDatabase()
    {
        var fixture = new DatabaseFixture();
        try
        {
            Guid calendarItemId;
            await using (var provider = BuildCalendarProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var calendar = scope.ServiceProvider.GetRequiredService<ICalendarItemApplicationService>();
                calendarItemId = (await calendar.CreateAsync(new CreateCalendarItemRequest(
                    "共享数据库",
                    string.Empty,
                    new DateOnly(2026, 9, 10),
                    new TimeOnly(9, 0),
                    "event-blue"))).Id;
            }

            await using (var provider = BuildProbeProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var records = scope.ServiceProvider.GetRequiredService<IRepository<SharedPluginRecord>>();
                var dbContext = scope.ServiceProvider.GetRequiredService<IDbContext>();
                await records.AddAsync(SharedPluginRecord.Create("another plugin"));
                await dbContext.SaveChangesAsync();
            }

            await using (var provider = BuildCalendarProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var calendar = scope.ServiceProvider.GetRequiredService<ICalendarItemApplicationService>();
                Assert.NotNull(await calendar.GetAsync(calendarItemId));
            }

            await using (var provider = BuildProbeProvider(fixture.DatabasePath))
            {
                await using var scope = await CreateInitializedScopeAsync(provider);
                var records = scope.ServiceProvider.GetRequiredService<IRepository<SharedPluginRecord>>();
                Assert.Equal("another plugin", records.AsNoTracking().Single().Value);
            }

            Assert.True(File.Exists(fixture.DatabasePath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static ServiceProvider BuildCalendarProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddQuantumPluginPersistence(databasePath);
        CalendarPluginInitializer.Initialize(services);
        return BuildProvider(services);
    }

    private static ServiceProvider BuildProbeProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddQuantumPluginPersistence(databasePath);
        services.AddSingleton<
            IDbContextModelCreatingContributor,
            SharedPluginModelCreatingContributor>();
        return BuildProvider(services);
    }

    private static ServiceProvider BuildProvider(IServiceCollection services)
        => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    private static async Task<AsyncServiceScope> CreateInitializedScopeAsync(
        ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<PluginDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None);
        return scope;
    }

    private static async Task<WeakReference> LoadAndUnloadCalendarPluginAsync(
        DatabaseFixture fixture)
    {
        var modulesRoot = Path.Combine(fixture.RootPath, "Modules");
        var pluginRoot = Path.Combine(modulesRoot, "quantum.plugin.example-calendar");
        var shadowRoot = Path.Combine(fixture.RootPath, "Shadow");
        Directory.CreateDirectory(pluginRoot);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Quantum.ExampleCalendarPlugin.dll"),
            Path.Combine(pluginRoot, "Quantum.ExampleCalendarPlugin.dll"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "plugin.json"),
            """
            {
              "id": "quantum.plugin.example-calendar",
              "version": "1.0.0",
              "entryAssembly": "Quantum.ExampleCalendarPlugin.dll"
            }
            """);

        using var hostServices = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddQuantumPluginEventBus()
            .BuildServiceProvider();
        var catalog = new PluginCatalog([]);
        await using var manager = new PluginRuntimeManager(
            catalog,
            new PluginRuntimeOptions(modulesRoot, shadowRoot, fixture.DatabasePath));
        await manager.InitializeAsync(hostServices);
        var plugin = Assert.Single(catalog.Plugins);
        var loadContext = AssemblyLoadContext.GetLoadContext(plugin.EntryAssembly!);
        Assert.NotNull(loadContext);
        var weakReference = new WeakReference(loadContext, trackResurrection: false);

        var result = await manager.UnloadAsync("quantum.plugin.example-calendar");
        Assert.True(result.Succeeded, result.Message);
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

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "quantum-calendar-tests",
            Guid.NewGuid().ToString("N"));

        public string DatabasePath => Path.Combine(_directory, "quantum.db");

        public string RootPath => _directory;

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class SharedPluginRecord
    {
        private SharedPluginRecord()
        {
        }

        private SharedPluginRecord(Guid id, string value)
        {
            Id = id;
            Value = value;
        }

        public Guid Id { get; private set; }

        public string Value { get; private set; } = string.Empty;

        public static SharedPluginRecord Create(string value)
            => new(Guid.NewGuid(), value);
    }

    private sealed class SharedPluginModelCreatingContributor
        : IDbContextModelCreatingContributor
    {
        public void Configure(IDbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SharedPluginRecord>(entity =>
            {
                entity.ToTable("SharedPluginRecords");
                entity.IsHostOnly();
                entity.HasKey(record => record.Id);
                entity.Property(record => record.Value).HasMaxLength(200).IsRequired();
            });
        }
    }
}
