using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NOF.Contract;
using Quantum.Plugins;

namespace Quantum.WebPlugins;

public sealed class WebPluginInteropBridge(
    PluginCatalog catalog,
    PluginRpcRouter rpcRouter,
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
                "rpc" => await InvokePluginRpcAsync(
                        plugin,
                        method,
                        arguments,
                        cancellation.Token)
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
                VersionRange = integration.VersionRange.ToString(),
                Active = target is not null
                    && integration.VersionRange.Contains(target.Manifest.Version)
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

    private async Task<JsonElement> InvokePluginRpcAsync(
        LoadedPlugin plugin,
        string method,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "invoke", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown plugin RPC method '{method}'.");
        }

        if (arguments.ValueKind != JsonValueKind.Object
            || OptionalString(arguments, "rpcName") is not { } rpcName)
        {
            return Serialize(Result.Fail(
                PluginRpcErrors.InvalidName,
                "RPC argument 'rpcName' is required."));
        }

        if (!arguments.TryGetProperty("payload", out var payload))
        {
            return Serialize(Result.Fail(
                PluginRpcErrors.InvalidPayload,
                "RPC argument 'payload' is required."));
        }

        var contextItems = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (arguments.TryGetProperty("context", out var context)
            && context.ValueKind != JsonValueKind.Null)
        {
            if (context.ValueKind != JsonValueKind.Object)
            {
                return Serialize(Result.Fail(
                    PluginRpcErrors.InvalidContext,
                    "RPC argument 'context' must be an object."));
            }
            foreach (var item in context.EnumerateObject())
            {
                if (PluginRpcInvoker.IsReservedContextKey(item.Name)
                    || !contextItems.TryAdd(item.Name, item.Value.Clone()))
                {
                    return Serialize(Result.Fail(
                        PluginRpcErrors.InvalidContext,
                        "RPC context keys must be unique and cannot use Quantum reserved keys."));
                }
            }
        }

        var invocation = await rpcRouter.InvokeAsync(
                rpcName,
                payload.Clone(),
                new PluginRpcCallContext(
                    (string)plugin.Manifest.Id,
                    plugin.RuntimeId.ToString("N"),
                    contextItems),
                cancellationToken)
            .ConfigureAwait(false);
        return invocation.Failure is not null
            ? Serialize(invocation.Failure)
            : invocation.SerializedResult!.Value;
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
