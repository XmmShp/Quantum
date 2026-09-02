using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;

namespace Quantum.Plugins;

internal sealed class PluginRpcRuntime : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PluginRpcSerializer _serializer;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IReadOnlyList<PluginRpcMethodDefinition> _methods = [];
    private TaskCompletionSource? _idle;
    private int _activeInvocations;
    private int _state;

    private PluginRpcRuntime(
        PluginId pluginId,
        Guid runtimeId,
        IServiceScopeFactory scopeFactory,
        PluginRpcSerializer serializer,
        ILogger logger)
    {
        PluginId = pluginId;
        RuntimeId = runtimeId;
        _scopeFactory = scopeFactory;
        _serializer = serializer;
        _logger = logger;
    }

    public PluginId PluginId { get; }

    public Guid RuntimeId { get; }

    public IReadOnlyList<PluginRpcMethodDefinition> Methods => _methods;

    public bool IsActive => Volatile.Read(ref _state) == (int)RpcRuntimeState.Active;

    public static PluginRpcRuntime Create(
        PluginId pluginId,
        Guid runtimeId,
        Assembly assembly,
        IServiceScopeFactory scopeFactory,
        PluginRpcSerializer serializer,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(logger);

        var runtime = new PluginRpcRuntime(
            pluginId,
            runtimeId,
            scopeFactory,
            serializer,
            logger);
        runtime._methods = DiscoverMethods(assembly);
        return runtime;
    }

    public void Resume()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state == (int)RpcRuntimeState.Disposed, this);
            _state = (int)RpcRuntimeState.Active;
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_gate)
        {
            if (_state == (int)RpcRuntimeState.Disposed)
            {
                return Task.CompletedTask;
            }

            _state = (int)RpcRuntimeState.Paused;
            if (_activeInvocations == 0)
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wait = _idle.Task;
        }

        return wait.WaitAsync(cancellationToken);
    }

    public async Task<PluginRpcDispatchResult> InvokeAsync(
        PluginRpcMethodDefinition method,
        JsonElement payload,
        PluginRpcCallContext callContext,
        CancellationToken cancellationToken)
    {
        if (!TryBeginInvocation())
        {
            return PluginRpcDispatchResult.Fail(
                PluginRpcErrors.NotFound,
                "The requested RPC service is not currently available.");
        }

        try
        {
            object? request;
            try
            {
                request = _serializer.Deserialize(payload, method.Mapping.RequestType);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return PluginRpcDispatchResult.Fail(
                    PluginRpcErrors.InvalidPayload,
                    $"The payload does not match RPC '{method.CanonicalName}'.");
            }

            if (request is null && method.Mapping.RequestType.IsValueType)
            {
                return PluginRpcDispatchResult.Fail(
                    PluginRpcErrors.InvalidPayload,
                    $"The payload does not match RPC '{method.CanonicalName}'.");
            }

            IResult result;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.ResolveDaemonServices();
                var handler = (RpcHandler)scope.ServiceProvider.GetRequiredService(
                    method.Mapping.HandlerType);
                var context = CreateTargetContext(callContext);
                result = await handler
                    .HandleAsync(request!, context, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    return PluginRpcDispatchResult.Success(
                        _serializer.Serialize(result, result.GetType()),
                        method.ReturnsValue);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    _logger.LogError(
                        exception,
                        "RPC result serialization failed for {RpcName} in plugin {PluginId} runtime {RuntimeId}.",
                        method.CanonicalName,
                        PluginId,
                        RuntimeId);
                    return PluginRpcDispatchResult.Fail(
                        PluginRpcErrors.InvalidResult,
                        "The RPC result could not be serialized.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogError(
                    exception,
                    "RPC handler {RpcHandler} failed for {RpcName} in plugin {PluginId} runtime {RuntimeId}.",
                    method.Mapping.HandlerType.FullName,
                    method.CanonicalName,
                    PluginId,
                    RuntimeId);
                return PluginRpcDispatchResult.Fail(
                    PluginRpcErrors.InvocationFailed,
                    "The RPC handler failed unexpectedly.");
            }
        }
        finally
        {
            EndInvocation();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await PauseAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_gate)
        {
            _state = (int)RpcRuntimeState.Disposed;
            _methods = [];
        }
    }

    private bool TryBeginInvocation()
    {
        lock (_gate)
        {
            if (_state != (int)RpcRuntimeState.Active)
            {
                return false;
            }

            checked
            {
                _activeInvocations++;
            }

            return true;
        }
    }

    private void EndInvocation()
    {
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            _activeInvocations--;
            if (_activeInvocations == 0 && _state != (int)RpcRuntimeState.Active)
            {
                idle = _idle;
                _idle = null;
            }
        }

        idle?.TrySetResult();
    }

    private static Context CreateTargetContext(PluginRpcCallContext source)
    {
        var items = source.Items.ToDictionary(
            static item => (object)item.Key,
            static item => (object?)item.Value.Clone());
        items[QuantumRpcContextKeys.CallerPluginId] = source.CallerPluginId;
        items[QuantumRpcContextKeys.CallerRuntimeId] = source.CallerRuntimeId;
        return Context.Empty.WithItems(items);
    }

    private static IReadOnlyList<PluginRpcMethodDefinition> DiscoverMethods(Assembly assembly)
    {
        var definitions = new List<PluginRpcMethodDefinition>();
        foreach (var serverType in GetLoadableTypes(assembly)
                     .Where(static type => !type.IsAbstract
                         && !type.ContainsGenericParameters
                         && typeof(IRpcServer).IsAssignableFrom(type))
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            var serviceType = serverType
                    .GetProperty(
                        nameof(IRpcServerServiceType.ServiceType),
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null) as Type
                ?? throw new InvalidOperationException(
                    $"RPC server '{serverType.FullName}' does not expose generated ServiceType metadata.");
            var handlerMappings = serverType
                    .GetProperty(
                        nameof(IRpcServer.HandlerMappings),
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    ?.GetValue(null) as IReadOnlyDictionary<string, RpcHandlerMapping>
                ?? throw new InvalidOperationException(
                    $"RPC server '{serverType.FullName}' does not expose generated HandlerMappings metadata.");
            if (serviceType.GetCustomAttribute<TransportOverQuantumAttribute>(inherit: false) is null)
            {
                continue;
            }

            var serviceName = serviceType
                    .GetCustomAttribute<RpcInvocationNameAttribute>(inherit: false)
                    ?.Name
                ?? GetDefaultServiceName(serviceType.Name);
            foreach (var mapping in handlerMappings.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                var contractMethod = serviceType.GetMethods()
                    .SingleOrDefault(method => string.Equals(
                            method.Name,
                            mapping.Key,
                            StringComparison.Ordinal)
                        && method.GetParameters() is [var parameter]
                        && parameter.ParameterType == mapping.Value.RequestType)
                    ?? throw new InvalidOperationException(
                        $"RPC server '{serverType.FullName}' maps unknown contract method "
                        + $"'{serviceType.FullName}.{mapping.Key}'.");
                var returnsValue = IsSupportedResult(contractMethod.ReturnType);
                var methodName = contractMethod
                        .GetCustomAttribute<RpcInvocationNameAttribute>(inherit: false)
                        ?.Name
                    ?? contractMethod.Name;
                var aliases = contractMethod
                    .GetCustomAttributes<RpcInvocationAliasAttribute>(inherit: false)
                    .Select(static attribute => attribute.Name)
                    .ToArray();
                definitions.Add(new PluginRpcMethodDefinition(
                    serviceName,
                    methodName,
                    aliases,
                    mapping.Value,
                    returnsValue,
                    $"{serverType.FullName}.{contractMethod.Name}"));
            }
        }

        return definitions;
    }

    private static bool IsSupportedResult(Type returnType)
    {
        if (returnType == typeof(Result))
        {
            return false;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"Quantum RPC method return type '{returnType}' must be Result or Result<T>.");
    }

    private static string GetDefaultServiceName(string interfaceName)
        => interfaceName.Length > 1
            && interfaceName[0] == 'I'
            && char.IsUpper(interfaceName[1])
                ? interfaceName[1..]
                : interfaceName;

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private enum RpcRuntimeState
    {
        Paused,
        Active,
        Disposed
    }
}

internal sealed record PluginRpcMethodDefinition(
    string ServiceName,
    string MethodName,
    IReadOnlyList<string> Aliases,
    RpcHandlerMapping Mapping,
    bool ReturnsValue,
    string Declaration)
{
    public string CanonicalName => $"{ServiceName}.{MethodName}";
}
