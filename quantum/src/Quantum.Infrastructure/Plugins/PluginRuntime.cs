using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quantum.Application.Plugins;
using Quantum.Domain.Plugins;
using Quantum.Plugin.Abstraction;

namespace Quantum.Infrastructure.Plugins;

internal sealed class PluginRuntime
{
    private readonly PluginLoadContext? _loadContext;
    private readonly ServiceProvider? _services;
    private readonly IReadOnlyList<IQuantumPlugin> _lifecycles;
    private readonly PluginEnvironmentProxy? _environment;
    private readonly bool[] _started;
    private readonly ILogger _logger;
    private bool _disposed;

    public PluginRuntime(
        PluginCandidate candidate,
        string shadowRootPath,
        PluginLoadContext? loadContext,
        ServiceProvider? services,
        IReadOnlyList<IQuantumPlugin> lifecycles,
        PluginEnvironmentProxy? environment,
        LoadedPlugin loadedPlugin,
        ILogger logger)
    {
        Candidate = candidate;
        ShadowRootPath = shadowRootPath;
        _loadContext = loadContext;
        _services = services;
        _lifecycles = lifecycles;
        _environment = environment;
        _started = new bool[lifecycles.Count];
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
        var services = _services;
        for (var index = 0; index < _lifecycles.Count; index++)
        {
            if (_started[index])
            {
                continue;
            }

            await _lifecycles[index]
                .StartAsync(services!, cancellationToken)
                .ConfigureAwait(false);
            _started[index] = true;
            _logger.LogInformation(
                "Started lifecycle {PluginLifecycle} for plugin {PluginId}.",
                _lifecycles[index].GetType().FullName,
                Candidate.Manifest.Id);
        }
    }

    public async Task<IReadOnlyList<Exception>> StopAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var services = _services;
        for (var index = _lifecycles.Count - 1; index >= 0; index--)
        {
            if (!_started[index])
            {
                continue;
            }

            try
            {
                await _lifecycles[index]
                    .StopAsync(services!, cancellationToken)
                    .ConfigureAwait(false);
                _started[index] = false;
                _logger.LogInformation(
                    "Stopped lifecycle {PluginLifecycle} for plugin {PluginId}.",
                    _lifecycles[index].GetType().FullName,
                    Candidate.Manifest.Id);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _logger.LogError(
                    exception,
                    "Lifecycle {PluginLifecycle} for plugin {PluginId} failed to stop.",
                    _lifecycles[index].GetType().FullName,
                    Candidate.Manifest.Id);
            }
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
            if (_services is not null)
            {
                await _services.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _loadContext?.Unload();
        }
    }

    public static PluginRuntime Create(
        PluginCandidate candidate,
        string sessionShadowRoot,
        PluginCatalog catalog,
        IServiceProvider hostServices,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(candidate);
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
                    loadContext: null,
                    services: null,
                    lifecycles: [],
                    environment: null,
                    loadedPlugin: webPlugin,
                    logger: logger);
            }

            var entryAssemblyPath = Path.Combine(shadowRoot, candidate.Manifest.Runtime.Entry);
            loadContext = new PluginLoadContext(entryAssemblyPath);
            var assembly = loadContext.LoadEntryAssembly();
            var routes = candidate.Manifest.Routes
                .Select(route => PluginRouteRegistration.Create(candidate.Manifest.Id, route, assembly))
                .ToArray();

            var serviceCollection = new ServiceCollection();
            var environment = new PluginEnvironmentProxy(catalog);
            serviceCollection.AddSingleton<IQuantumPluginEnvironment>(environment);
            serviceCollection.AddSingleton<IQuantumPluginRuntimeContext>(new PluginRuntimeContext(
                new QuantumPluginInfo(
                    candidate.Manifest.Id.Value,
                    candidate.Manifest.Version.ToString()),
                shadowRoot));
            CopyHostService<ILoggerFactory>(hostServices, serviceCollection);
            serviceCollection.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            InitializePluginServices(assembly, serviceCollection);
            services = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            var lifecycles = services.GetServices<IQuantumPlugin>().Distinct().ToArray();
            var loadedPlugin = new LoadedPlugin(
                candidate.Manifest,
                shadowRoot,
                assembly,
                routes,
                runtimeId,
                services);
            return new PluginRuntime(
                candidate,
                shadowRoot,
                loadContext,
                services,
                lifecycles,
                environment,
                loadedPlugin,
                logger);
        }
        catch
        {
            services?.Dispose();
            loadContext?.Unload();
            PluginShadowCopy.TryDelete(shadowRoot);
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
                    $"Plugin initializer '{type.FullName}' failed.",
                    exception.InnerException);
            }
        }
    }

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
