using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Quantum.Plugins;

namespace Quantum.WebPlugins;

public sealed class WebPluginInteropBridge(
    PluginCatalog catalog,
    IServiceProvider hostServices,
    NavigationManager navigation,
    IJSRuntime javaScript,
    QuantumPluginEventBusFactory eventBusFactory,
    ILogger<WebPluginInteropBridge> logger) : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _invocations = new();
    private readonly ConcurrentDictionary<string, Lazy<WebEventBusRuntime>> _eventBuses = new();
    private bool _disposed;

    [JSInvokable]
    public async Task<JsonElement> InvokeAsync(
        string pluginId,
        string runtimeId,
        string requestId,
        string capability,
        string method,
        JsonElement arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var plugin = catalog.FindPlugin(pluginId)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not loaded.");
        if (plugin.Manifest.Runtime.Kind != PluginRuntimeKind.Web)
        {
            throw new InvalidOperationException($"Plugin '{pluginId}' is not a Web plugin.");
        }

        if (!string.Equals(plugin.RuntimeId.ToString("N"), runtimeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Web plugin runtime '{runtimeId}' is stale and can no longer call host capabilities.");
        }

        var invocationKey = CreateInvocationKey(pluginId, runtimeId, requestId);
        using var cancellation = new CancellationTokenSource(InvocationTimeout);
        if (!_invocations.TryAdd(invocationKey, cancellation))
        {
            throw new InvalidOperationException($"RPC request '{requestId}' is already running.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        logger.LogDebug(
            "Web RPC {Capability}.{Method} started for {PluginId}.",
            capability,
            method,
            pluginId);
        try
        {
            var result = capability switch
            {
                "log" => InvokeLog(pluginId, method, arguments),
                "environment" => InvokeEnvironment(plugin, method),
                "navigation" => InvokeNavigation(method, arguments),
                "eventBus" => await InvokeEventBusAsync(
                        plugin,
                        runtimeId,
                        method,
                        arguments,
                        cancellation.Token)
                    .ConfigureAwait(false),
                "assets" => await InvokeAssetsAsync(plugin, method, arguments, cancellation.Token)
                    .ConfigureAwait(false),
                "dotnet" => await InvokeDotNetAsync(method, arguments, cancellation.Token)
                    .ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown Web plugin capability '{capability}'.")
            };
            logger.LogDebug(
                "Web RPC {Capability}.{Method} completed for {PluginId} in {ElapsedMilliseconds:F0} ms.",
                capability,
                method,
                pluginId,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogDebug(
                exception,
                "Web RPC {Capability}.{Method} failed for {PluginId}.",
                capability,
                method,
                pluginId);
            throw;
        }
        finally
        {
            _invocations.TryRemove(invocationKey, out _);
        }
    }

    [JSInvokable]
    public Task CancelAsync(string pluginId, string runtimeId, string requestId)
    {
        if (_invocations.TryGetValue(CreateInvocationKey(pluginId, runtimeId, requestId), out var cancellation))
        {
            TryCancel(cancellation);
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public async ValueTask ReleaseRuntimeAsync(string pluginId, string runtimeId)
    {
        if (_eventBuses.TryRemove(CreateRuntimeKey(pluginId, runtimeId), out var runtime)
            && runtime.IsValueCreated)
        {
            await runtime.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    [JSInvokable]
    public void LogHostWarning(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        logger.LogWarning("Web plugin host: {HostMessage}", message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cancellation in _invocations.Values)
        {
            TryCancel(cancellation);
        }

        _invocations.Clear();
        foreach (var runtime in _eventBuses.Values)
        {
            if (runtime.IsValueCreated)
            {
                runtime.Value.Dispose();
            }
        }

        _eventBuses.Clear();
    }

    private JsonElement InvokeLog(string pluginId, string method, JsonElement arguments)
    {
        var message = RequireString(arguments, "message");
        var level = method.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            _ => throw new InvalidOperationException($"Unknown log level '{method}'.")
        };
        logger.Log(level, "Web plugin {PluginId}: {PluginMessage}", pluginId, message);
        return Serialize(null);
    }

    private JsonElement InvokeEnvironment(LoadedPlugin plugin, string method)
    {
        if (!string.Equals(method, "snapshot", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown environment method '{method}'.");
        }

        var integrations = plugin.Manifest.Integrations.Select(integration =>
        {
            var target = catalog.FindPlugin((string)integration.Id);
            return new
            {
                PluginId = (string)integration.Id,
                MinimumVersion = integration.MinimumVersion.ToString(),
                Active = target is not null
                    && target.Manifest.Version.CompareTo(integration.MinimumVersion) >= 0
            };
        });
        return Serialize(new
        {
            Plugin = new
            {
                Id = (string)plugin.Manifest.Id,
                Version = plugin.Manifest.Version.ToString(),
                RuntimeId = plugin.RuntimeId.ToString("N")
            },
            catalog.LoadedPlugins,
            Integrations = integrations
        });
    }

    private JsonElement InvokeNavigation(string method, JsonElement arguments)
    {
        if (!string.Equals(method, "navigate", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown navigation method '{method}'.");
        }

        var href = RequireString(arguments, "href");
        var isRootRelative = href.StartsWith("/", StringComparison.Ordinal)
            && !href.StartsWith("//", StringComparison.Ordinal);
        if (!string.Equals(href, href.Trim(), StringComparison.Ordinal)
            || href.Contains('\\', StringComparison.Ordinal)
            || href.StartsWith("//", StringComparison.Ordinal)
            || (!isRootRelative && Uri.TryCreate(href, UriKind.Absolute, out _)))
        {
            throw new InvalidOperationException("Web plugins can only navigate to host-relative URLs.");
        }

        navigation.NavigateTo(href);
        return Serialize(null);
    }

    private async Task<JsonElement> InvokeEventBusAsync(
        LoadedPlugin plugin,
        string runtimeId,
        string method,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var runtime = GetOrCreateEventBusRuntime(plugin, runtimeId);
        if (string.Equals(method, "publish", StringComparison.Ordinal))
        {
            var topic = QuantumTopic.Of(RequireString(arguments, "topic"));
            var payload = RequireProperty(arguments, "payload");
            await runtime.Bus.CreatePublisher<JsonElement>(topic)
                .PublishAsync(payload, cancellationToken)
                .ConfigureAwait(false);
            return Serialize(null);
        }

        var subscriptionId = RequireString(arguments, "subscriptionId");
        if (string.Equals(method, "subscribe", StringComparison.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topic = QuantumTopic.Of(RequireString(arguments, "topic"));
            var subscription = runtime.Bus.Subscribe(
                topic,
                (@event, eventCancellationToken) => DispatchEventAsync(
                    (string)plugin.Manifest.Id,
                    runtimeId,
                    subscriptionId,
                    @event,
                    eventCancellationToken));
            if (!runtime.TryAdd(subscriptionId, subscription))
            {
                subscription.Dispose();
                throw new InvalidOperationException(
                    $"EventBus subscription '{subscriptionId}' already exists.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                runtime.Remove(subscriptionId);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Serialize(null);
        }

        if (string.Equals(method, "unsubscribe", StringComparison.Ordinal))
        {
            runtime.Remove(subscriptionId);
            return Serialize(null);
        }

        throw new InvalidOperationException($"Unknown EventBus method '{method}'.");
    }

    private WebEventBusRuntime GetOrCreateEventBusRuntime(LoadedPlugin plugin, string runtimeId)
    {
        var runtime = _eventBuses.GetOrAdd(
            CreateRuntimeKey((string)plugin.Manifest.Id, runtimeId),
            _ => new Lazy<WebEventBusRuntime>(
                () => new WebEventBusRuntime(eventBusFactory.Create(
                    new QuantumPluginInfo(
                        plugin.Manifest.Id,
                        plugin.Manifest.Version))),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return runtime.Value;
    }

    private async Task DispatchEventAsync(
        string pluginId,
        string runtimeId,
        string subscriptionId,
        QuantumEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await javaScript.InvokeVoidAsync(
                    "quantum.plugins.dispatchEvent",
                    cancellationToken,
                    pluginId,
                    runtimeId,
                    subscriptionId,
                    @event)
                .ConfigureAwait(false);
        }
        catch (JSException exception) when (!IsCurrentRuntime(pluginId, runtimeId))
        {
            // A replacement runtime is activated before the old iframe is disposed so the UI
            // can fall back if activation fails. During that overlap, a new publisher can reach
            // an old EventBus subscription whose handler is already forbidden from making RPCs.
            // The obsolete delivery must not make the replacement runtime fail activation.
            logger.LogDebug(
                exception,
                "Ignored EventBus delivery failure from stale Web plugin runtime {PluginId}@{RuntimeId}.",
                pluginId,
                runtimeId);
        }
    }

    private bool IsCurrentRuntime(string pluginId, string runtimeId)
        => catalog.FindPlugin(pluginId) is { } plugin
            && string.Equals(plugin.RuntimeId.ToString("N"), runtimeId, StringComparison.Ordinal);

    private static async Task<JsonElement> InvokeAssetsAsync(
        LoadedPlugin plugin,
        string method,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "readText", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown assets method '{method}'.");
        }

        var relativePath = RequireString(arguments, "path");
        var path = ResolvePluginAssetPath(plugin, relativePath);
        var information = new FileInfo(path);
        if (!information.Exists)
        {
            throw new FileNotFoundException($"Plugin asset '{relativePath}' was not found.", path);
        }

        if (information.Length > 2 * 1024 * 1024)
        {
            throw new InvalidOperationException("Text assets read through RPC cannot exceed 2 MiB.");
        }

        return Serialize(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
    }

    private async Task<JsonElement> InvokeDotNetAsync(
        string method,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "invoke", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown .NET interop method '{method}'.");
        }

        var target = OptionalString(arguments, "target") ?? "host";
        var serviceTypeName = RequireString(arguments, "service");
        var methodName = RequireString(arguments, "method");

        var provider = ResolveTargetProvider(target);
        AsyncServiceScope? scope = null;
        if (provider.GetService<IServiceScopeFactory>() is { } scopeFactory)
        {
            var ownedScope = scopeFactory.CreateAsyncScope();
            ownedScope.ServiceProvider.ResolveDaemonServices();
            scope = ownedScope;
        }
        try
        {
            var effectiveProvider = scope?.ServiceProvider ?? provider;
            var service = effectiveProvider.GetService(serviceTypeName)
                ?? throw new InvalidOperationException(
                    $"Service '{serviceTypeName}' is not registered in target '{target}'.");
            var argumentArray = arguments.TryGetProperty("arguments", out var values)
                ? values
                : EmptyJsonArray;
            var parameterTypes = ReadStringArray(arguments, "parameterTypes");
            var invocation = BindMethod(
                ResolveContractType(service, serviceTypeName),
                methodName,
                argumentArray,
                parameterTypes,
                cancellationToken);

            object? result;
            try
            {
                result = invocation.Method.Invoke(service, invocation.Arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException(exception.InnerException.Message, exception.InnerException);
            }

            var asynchronousResult = NormalizeResult(result, invocation.Method.ReturnType);
            if (asynchronousResult.Completion is { } completion)
            {
                try
                {
                    await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!completion.IsCompleted && scope is { } deferredScope)
                {
                    // WaitAsync does not stop a service method that ignores CancellationToken. Keep
                    // its scope alive until the underlying operation actually settles.
                    scope = null;
                    _ = DisposeScopeWhenCompletedAsync(completion, deferredScope);
                    throw;
                }
            }

            result = asynchronousResult.GetResult();
            return Serialize(result);
        }
        finally
        {
            if (scope is { } ownedScope)
            {
                await ownedScope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private IServiceProvider ResolveTargetProvider(string target)
    {
        if (string.Equals(target, "host", StringComparison.Ordinal))
        {
            return hostServices;
        }

        var targetPlugin = catalog.FindPlugin(target)
            ?? throw new InvalidOperationException($"Target plugin '{target}' is not loaded.");
        return targetPlugin.Services
            ?? throw new InvalidOperationException(
                $"Target plugin '{target}' does not expose a .NET service provider.");
    }

    private static BoundInvocation BindMethod(
        Type contractType,
        string methodName,
        JsonElement arguments,
        IReadOnlyList<string>? parameterTypes,
        CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(".NET invocation arguments must be a JSON array.");
        }

        var suppliedArguments = arguments.EnumerateArray().Select(static item => item.Clone()).ToArray();
        var matches = new List<BoundInvocation>();
        foreach (var method in contractType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                         && !candidate.ContainsGenericParameters
                         && !candidate.IsSpecialName))
        {
            var parameters = method.GetParameters();
            var serializableParameters = parameters
                .Where(static parameter => parameter.ParameterType != typeof(CancellationToken))
                .ToArray();
            if (serializableParameters.Length != suppliedArguments.Length
                || parameters.Any(static parameter => parameter.ParameterType.IsByRef
                    || parameter.ParameterType.IsPointer))
            {
                continue;
            }

            if (parameterTypes is not null
                && (parameterTypes.Count != serializableParameters.Length
                    || parameterTypes.Where((name, index) => !string.Equals(
                        name,
                        serializableParameters[index].ParameterType.FullName,
                        StringComparison.Ordinal)).Any()))
            {
                continue;
            }

            var boundArguments = new object?[parameters.Length];
            var suppliedIndex = 0;
            var failed = false;
            for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    boundArguments[parameterIndex] = cancellationToken;
                    continue;
                }

                try
                {
                    boundArguments[parameterIndex] = suppliedArguments[suppliedIndex]
                        .Deserialize(parameter.ParameterType, SerializerOptions);
                    suppliedIndex++;
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    failed = true;
                    break;
                }
            }

            if (!failed)
            {
                matches.Add(new BoundInvocation(method, boundArguments));
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new MissingMethodException(
                $"No callable overload '{contractType.FullName}.{methodName}' matches the supplied arguments."),
            _ => throw new AmbiguousMatchException(
                $"More than one overload '{contractType.FullName}.{methodName}' matches; supply parameterTypes.")
        };
    }

    private static Type ResolveContractType(object service, string serviceTypeName)
    {
        var implementationType = service.GetType();
        if (string.Equals(implementationType.FullName, serviceTypeName, StringComparison.Ordinal))
        {
            return implementationType;
        }

        return implementationType.GetInterfaces().SingleOrDefault(candidate => string.Equals(
                candidate.FullName,
                serviceTypeName,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Resolved service '{implementationType.FullName}' does not implement '{serviceTypeName}'.");
    }

    private static NormalizedResult NormalizeResult(object? result, Type returnType)
    {
        if (returnType == typeof(void))
        {
            return new NormalizedResult(Completion: null, static () => null);
        }

        if (result is Task task)
        {
            return new NormalizedResult(
                task,
                () => returnType.IsGenericType ? returnType.GetProperty("Result")?.GetValue(result) : null);
        }

        if (returnType == typeof(ValueTask))
        {
            return new NormalizedResult(((ValueTask)result!).AsTask(), static () => null);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var taskResult = (Task)returnType.GetMethod("AsTask")!.Invoke(result, null)!;
            return new NormalizedResult(
                taskResult,
                () => taskResult.GetType().GetProperty("Result")?.GetValue(taskResult));
        }

        return new NormalizedResult(Completion: null, () => result);
    }

    private async Task DisposeScopeWhenCompletedAsync(Task completion, AsyncServiceScope scope)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The RPC caller already observed cancellation or timeout. The underlying service
            // failure is intentionally observed here so it cannot become unobserved.
        }

        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Could not dispose a deferred Web plugin RPC scope.");
        }
    }

    private static string ResolvePluginAssetPath(LoadedPlugin plugin, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Plugin asset paths must be relative and use forward slashes.");
        }

        var root = Path.GetFullPath(Path.Combine(plugin.RootPath, "wwwroot"));
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Plugin asset path escapes the plugin wwwroot directory.");
        }

        return candidate;
    }

    private static string RequireString(JsonElement arguments, string propertyName)
        => OptionalString(arguments, propertyName)
            ?? throw new InvalidOperationException($"RPC argument '{propertyName}' is required.");

    private static string? OptionalString(JsonElement arguments, string propertyName)
        => arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static JsonElement RequireProperty(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidOperationException($"RPC argument '{propertyName}' is required.");
        }

        return value.Clone();
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(propertyName, out var values)
            || values.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"RPC argument '{propertyName}' must be an array.");
        }

        return values.EnumerateArray()
            .Select(value => value.GetString()
                ?? throw new InvalidOperationException($"RPC argument '{propertyName}' must contain strings."))
            .ToArray();
    }

    private static JsonElement Serialize(object? value)
        => JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), SerializerOptions);

    private static string CreateInvocationKey(string pluginId, string runtimeId, string requestId)
        => $"{pluginId}\n{runtimeId}\n{requestId}";

    private static string CreateRuntimeKey(string pluginId, string runtimeId)
        => $"{pluginId}\n{runtimeId}";

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static JsonElement EmptyJsonArray { get; } = JsonSerializer.SerializeToElement(Array.Empty<object>());

    private sealed record BoundInvocation(MethodInfo Method, object?[] Arguments);

    private sealed record NormalizedResult(Task? Completion, Func<object?> GetResult);

    private sealed class WebEventBusRuntime(QuantumPluginEventBusHandle bus)
        : IDisposable, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, IQuantumSubscription> _subscriptions = new();

        public QuantumPluginEventBusHandle Bus { get; } = bus;

        public bool TryAdd(string id, IQuantumSubscription subscription)
            => _subscriptions.TryAdd(id, subscription);

        public void Remove(string id)
        {
            if (_subscriptions.TryRemove(id, out var subscription))
            {
                subscription.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
            Bus.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var subscription in _subscriptions.Values)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }

            _subscriptions.Clear();
            await Bus.DisposeAsync().ConfigureAwait(false);
        }
    }
}
