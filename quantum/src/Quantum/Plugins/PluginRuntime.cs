using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NOF.Infrastructure;
using Quantum.Plugin.Abstraction;
using Quantum.Plugins.Persistence;

namespace Quantum.Plugins;

internal sealed class PluginRuntime
{
    private readonly PluginLoadContext? _loadContext;
    private readonly ServiceProvider? _services;
    private readonly AsyncServiceScope? _lifecycleScope;
    private readonly PluginEnvironmentProxy? _environment;
    private readonly PluginEventBus? _eventBus;
    private readonly IReadOnlyList<PluginBootstrap> _bootstraps;
    private readonly string _databasePath;
    private readonly bool _usesDatabase;
    private readonly ILogger _logger;
    private readonly bool[] _started;
    private bool _migrationsApplied;
    private bool _servicesInitialized;
    private bool _disposed;

    private PluginRuntime(
        PluginCandidate candidate,
        string shadowRootPath,
        string databasePath,
        PluginLoadContext? loadContext,
        ServiceProvider? services,
        AsyncServiceScope? lifecycleScope,
        PluginEnvironmentProxy? environment,
        PluginEventBus? eventBus,
        IReadOnlyList<PluginBootstrap> bootstraps,
        bool usesDatabase,
        LoadedPlugin loadedPlugin,
        ILogger logger)
    {
        Candidate = candidate;
        ShadowRootPath = shadowRootPath;
        _databasePath = databasePath;
        _loadContext = loadContext;
        _services = services;
        _lifecycleScope = lifecycleScope;
        _environment = environment;
        _eventBus = eventBus;
        _bootstraps = bootstraps;
        _usesDatabase = usesDatabase;
        _started = new bool[bootstraps.Count];
        LoadedPlugin = loadedPlugin;
        _logger = logger;
    }

    public PluginCandidate Candidate { get; }

    public string ShadowRootPath { get; }

    public LoadedPlugin LoadedPlugin { get; }

    public WeakReference? LoadContextReference => _loadContext is null
        ? null
        : new WeakReference(_loadContext, trackResurrection: false);

    public void UseEnvironment(IQuantumPluginEnvironment environment)
    {
        if (_environment is not null)
        {
            _environment.Target = environment;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_migrationsApplied)
        {
            await PluginDatabaseMigrator.ApplyAsync(
                    Candidate.Manifest,
                    ShadowRootPath,
                    _databasePath,
                    cancellationToken)
                .ConfigureAwait(false);
            _migrationsApplied = true;
        }

        var services = _lifecycleScope?.ServiceProvider;
        if (services is null)
        {
            return;
        }

        if (!_servicesInitialized)
        {
            services.ResolveDaemonServices();
            if (_usesDatabase && Candidate.Manifest.Database is null)
            {
                await services
                    .GetRequiredService<PluginDatabaseInitializer>()
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            _servicesInitialized = true;
        }

        _eventBus?.Resume();
        try
        {
            for (var index = 0; index < _bootstraps.Count; index++)
            {
                if (_started[index])
                {
                    continue;
                }

                await _bootstraps[index]
                    .StartAsync(services, cancellationToken)
                    .ConfigureAwait(false);
                _started[index] = true;
                _logger.LogInformation(
                    "Started lifecycle bootstrap {PluginBootstrap} for plugin {PluginId}.",
                    _bootstraps[index].Type.FullName,
                    Candidate.Manifest.Id);
            }
        }
        catch
        {
            _eventBus?.Pause();
            throw;
        }
    }

    public async Task<IReadOnlyList<Exception>> StopAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var services = _lifecycleScope?.ServiceProvider;
        if (services is null)
        {
            _eventBus?.Pause();
            return failures;
        }

        if (_started.Any(static started => started))
        {
            // A failed StartAsync pauses the candidate while it waits for rollback cleanup.
            // Reactivate it only for the already-started lifecycle hooks so they can publish
            // their normal shutdown events.
            _eventBus?.Resume();
        }

        for (var index = _bootstraps.Count - 1; index >= 0; index--)
        {
            if (!_started[index])
            {
                continue;
            }

            try
            {
                await _bootstraps[index]
                    .StopAsync(services, cancellationToken)
                    .ConfigureAwait(false);
                _started[index] = false;
                _logger.LogInformation(
                    "Stopped lifecycle bootstrap {PluginBootstrap} for plugin {PluginId}.",
                    _bootstraps[index].Type.FullName,
                    Candidate.Manifest.Id);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _logger.LogError(
                    exception,
                    "Lifecycle bootstrap {PluginBootstrap} for plugin {PluginId} failed to stop.",
                    _bootstraps[index].Type.FullName,
                    Candidate.Manifest.Id);
            }
        }

        if (failures.Count == 0)
        {
            _eventBus?.Pause();
        }

        return failures;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            try
            {
                if (_lifecycleScope is { } lifecycleScope)
                {
                    await lifecycleScope.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (_services is not null)
                {
                    await _services.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _loadContext?.Unload();
        }
    }

    public static async Task<PluginRuntime> CreateAsync(
        PluginCandidate candidate,
        string sessionShadowRoot,
        string databasePath,
        PluginCatalog catalog,
        IServiceProvider hostServices,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hostServices);

        var runtimeId = Guid.NewGuid();
        var shadowRoot = Path.Combine(
            sessionShadowRoot,
            candidate.Manifest.Id.Value,
            runtimeId.ToString("N"));
        PluginShadowCopy.Copy(candidate.RootPath, shadowRoot);

        PluginLoadContext? loadContext = null;
        ServiceProvider? services = null;
        AsyncServiceScope? lifecycleScope = null;
        try
        {
            if (candidate.Manifest.Runtime.Kind == PluginRuntimeKind.Web)
            {
                var entryModulePath = Path.Combine(
                    shadowRoot,
                    "wwwroot",
                    candidate.Manifest.Runtime.Entry.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(entryModulePath))
                {
                    throw new FileNotFoundException(
                        $"Web entry module '{candidate.Manifest.Runtime.Entry}' was not copied into the runtime shadow.",
                        entryModulePath);
                }

                var webRoutes = candidate.Manifest.Routes
                    .Select(route => PluginRouteRegistration.CreateWeb(candidate.Manifest.Id, route))
                    .ToArray();
                var webPlugin = new LoadedPlugin(
                    candidate.Manifest,
                    shadowRoot,
                    entryAssembly: null,
                    routes: webRoutes,
                    runtimeId: runtimeId,
                    services: null);
                return new PluginRuntime(
                    candidate,
                    shadowRoot,
                    databasePath,
                    loadContext: null,
                    services: null,
                    lifecycleScope: null,
                    environment: null,
                    eventBus: null,
                    bootstraps: [],
                    usesDatabase: false,
                    loadedPlugin: webPlugin,
                    logger: logger);
            }

            var entryAssemblyPath = Path.Combine(shadowRoot, candidate.Manifest.Runtime.Entry);
            loadContext = new PluginLoadContext(entryAssemblyPath);
            var assembly = loadContext.LoadEntryAssembly();
            var bootstraps = DiscoverPluginBootstraps(assembly);
            var routes = candidate.Manifest.Routes
                .Select(route => PluginRouteRegistration.Create(candidate.Manifest.Id, route, assembly))
                .ToArray();

            var serviceCollection = new ServiceCollection();
            var environment = new PluginEnvironmentProxy(catalog);
            var pluginInfo = new QuantumPluginInfo(
                candidate.Manifest.Id.Value,
                candidate.Manifest.Version.ToString());
            var eventHub = hostServices.GetRequiredService<PluginEventHub>();
            serviceCollection.AddSingleton<IQuantumPluginEnvironment>(environment);
            serviceCollection.AddSingleton<IQuantumPluginRuntimeContext>(new PluginRuntimeContext(
                pluginInfo,
                shadowRoot));
            serviceCollection.AddSingleton<IQuantumEventBus>(
                _ => new PluginEventBus(pluginInfo, eventHub));
            CopyHostService<ILoggerFactory>(hostServices, serviceCollection);
            serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            InitializePluginServices(assembly, serviceCollection);
            var usesDatabase = serviceCollection.Any(static descriptor =>
                descriptor.ServiceType == typeof(IDbContextModelCreatingContributor));
            if (usesDatabase)
            {
                serviceCollection.AddQuantumPluginPersistence(databasePath);
            }
            services = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var eventBus = (PluginEventBus)services.GetRequiredService<IQuantumEventBus>();

            var ownedScope = services.CreateAsyncScope();
            ownedScope.ServiceProvider.ResolveDaemonServices(); lifecycleScope = ownedScope;
            var loadedPlugin = new LoadedPlugin(
                candidate.Manifest,
                shadowRoot,
                assembly,
                routes,
                runtimeId,
                ownedScope.ServiceProvider);
            return new PluginRuntime(
                candidate,
                shadowRoot,
                databasePath,
                loadContext,
                services,
                lifecycleScope,
                environment,
                eventBus,
                bootstraps,
                usesDatabase,
                loadedPlugin,
                logger);
        }
        catch
        {
            try
            {
                if (lifecycleScope is { } ownedScope)
                {
                    await ownedScope.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (services is not null)
                    {
                        await services.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    loadContext?.Unload();
                    PluginShadowCopy.TryDelete(shadowRoot);
                }
            }

            throw;
        }
    }

    private static void CopyHostService<TService>(
        IServiceProvider hostServices,
        IServiceCollection pluginServices)
        where TService : class
    {
        if (hostServices.GetService(typeof(TService)) is TService service)
        {
            pluginServices.AddSingleton(service);
        }
    }

    private static void InitializePluginServices(Assembly assembly, IServiceCollection services)
    {
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (!type.GetInterfaces().Any(static implemented =>
                    string.Equals(
                        implemented.FullName,
                        "NOF.Abstraction.IAssemblyInitializer",
                        StringComparison.Ordinal)))
            {
                continue;
            }

            var initialize = type.GetMethod(
                "Initialize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IServiceCollection)],
                modifiers: null);
            if (initialize is null)
            {
                throw new InvalidOperationException(
                    $"Plugin initializer '{type.FullName}' does not expose Initialize(IServiceCollection).");
            }

            try
            {
                initialize.Invoke(null, [services]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException(
                    $"Plugin initializer '{type.FullName}' failed: {exception.InnerException.Message}",
                    exception.InnerException);
            }
        }
    }

    private static IReadOnlyList<PluginBootstrap> DiscoverPluginBootstraps(Assembly assembly)
        => GetLoadableTypes(assembly)
            .Where(static type => !type.IsInterface && typeof(IQuantumPlugin).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(PluginBootstrap.Create)
            .ToArray();

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            throw new InvalidOperationException(
                $"Plugin assembly '{assembly.FullName}' contains types that could not be loaded.",
                exception);
        }
    }

    private sealed record PluginRuntimeContext(
        QuantumPluginInfo Plugin,
        string RootPath) : IQuantumPluginRuntimeContext;

    private sealed record PluginBootstrap(
        Type Type,
        Func<IServiceProvider, CancellationToken, Task> StartAsync,
        Func<IServiceProvider, CancellationToken, Task> StopAsync)
    {
        public static PluginBootstrap Create(Type type)
        {
            var invokerType = typeof(PluginBootstrapInvoker<>).MakeGenericType(type);
            return new PluginBootstrap(
                type,
                CreateDelegate(invokerType, nameof(StartAsync)),
                CreateDelegate(invokerType, nameof(StopAsync)));
        }

        private static Func<IServiceProvider, CancellationToken, Task> CreateDelegate(
            Type invokerType,
            string methodName)
            => (Func<IServiceProvider, CancellationToken, Task>)invokerType
                .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate(typeof(Func<IServiceProvider, CancellationToken, Task>));
    }

    private static class PluginBootstrapInvoker<TPlugin>
        where TPlugin : IQuantumPlugin
    {
        public static Task StartAsync(
            IServiceProvider services,
            CancellationToken cancellationToken)
            => TPlugin.StartAsync(services, cancellationToken);

        public static Task StopAsync(
            IServiceProvider services,
            CancellationToken cancellationToken)
            => TPlugin.StopAsync(services, cancellationToken);
    }
}

internal sealed class PluginEnvironmentProxy(IQuantumPluginEnvironment target)
    : IQuantumPluginEnvironment
{
    private IQuantumPluginEnvironment _target = target;

    public IQuantumPluginEnvironment Target
    {
        private get => Volatile.Read(ref _target);
        set => Volatile.Write(
            ref _target,
            value ?? throw new ArgumentNullException(nameof(value)));
    }

    public IReadOnlyList<QuantumPluginInfo> LoadedPlugins => Target.LoadedPlugins;

    public bool IsPluginLoaded(string pluginId) => Target.IsPluginLoaded(pluginId);

    public bool IsIntegrationActive(string ownerPluginId, string targetPluginId)
        => Target.IsIntegrationActive(ownerPluginId, targetPluginId);
}

internal static class PluginShadowCopy
{
    public static void Copy(string sourceRoot, string destinationRoot)
    {
        var source = new DirectoryInfo(Path.GetFullPath(sourceRoot));
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Plugin source directory '{source.FullName}' does not exist.");
        }

        CopyDirectory(source, new DirectoryInfo(Path.GetFullPath(destinationRoot)));
    }

    public static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
    {
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Plugin directory '{source.FullName}' cannot be a symbolic link or reparse point.");
        }

        destination.Create();
        foreach (var file in source.EnumerateFiles().OrderBy(static file => file.Name, StringComparer.Ordinal))
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Plugin file '{file.FullName}' cannot be a symbolic link or reparse point.");
            }

            file.CopyTo(Path.Combine(destination.FullName, file.Name), overwrite: false);
        }

        foreach (var directory in source.EnumerateDirectories().OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            CopyDirectory(directory, new DirectoryInfo(Path.Combine(destination.FullName, directory.Name)));
        }
    }
}
