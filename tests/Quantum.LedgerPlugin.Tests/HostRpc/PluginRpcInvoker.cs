using System.Text.Json;
using NOF.Contract;

namespace Quantum.Plugins;

internal sealed class PluginRpcInvoker(
    PluginId callerPluginId,
    Guid callerRuntimeId,
    PluginRpcSerializer serializer) : IRpcInvoker, IDisposable
{
    private PluginRpcRegistry? _registry;
    private int _disposed;

    public void UseRegistry(PluginRpcRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Volatile.Write(ref _registry, registry);
    }

    public async Task<Result<TResponse>> InvokeAsync<TResponse>(
        string rpcName,
        object payload,
        Context context,
        CancellationToken cancellationToken = default)
    {
        var request = PrepareRequest(payload, context);
        if (request.Failure is not null)
        {
            return Result<TResponse>.From(request.Failure);
        }

        var registry = Volatile.Read(ref _registry);
        if (registry is null)
        {
            return Result<TResponse>.From(Result.Fail(
                PluginRpcErrors.Unavailable,
                "The plugin RPC router is not available."));
        }

        var invocation = await registry.InvokeAsync(
                rpcName,
                request.Payload!.Value,
                request.Context!,
                expectsValue: true,
                cancellationToken)
            .ConfigureAwait(false);
        if (invocation.Failure is not null)
        {
            return Result<TResponse>.From(invocation.Failure);
        }

        try
        {
            return serializer.Deserialize<Result<TResponse>>(invocation.SerializedResult!.Value)
                ?? Result<TResponse>.From(Result.Fail(
                    PluginRpcErrors.InvalidResult,
                    "The RPC result was empty."));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result<TResponse>.From(Result.Fail(
                PluginRpcErrors.InvalidResult,
                "The RPC result does not match the requested response type."));
        }
    }

    public async Task<Result> InvokeAsync(
        string rpcName,
        object payload,
        Context context,
        CancellationToken cancellationToken = default)
    {
        var request = PrepareRequest(payload, context);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var registry = Volatile.Read(ref _registry);
        if (registry is null)
        {
            return Result.Fail(
                PluginRpcErrors.Unavailable,
                "The plugin RPC router is not available.");
        }

        var invocation = await registry.InvokeAsync(
                rpcName,
                request.Payload!.Value,
                request.Context!,
                expectsValue: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (invocation.Failure is not null)
        {
            return invocation.Failure;
        }

        try
        {
            return serializer.Deserialize<Result>(invocation.SerializedResult!.Value)
                ?? Result.Fail(PluginRpcErrors.InvalidResult, "The RPC result was empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result.Fail(
                PluginRpcErrors.InvalidResult,
                "The RPC result does not match the requested response type.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Volatile.Write(ref _registry, null);
        }
    }

    private PreparedRpcRequest PrepareRequest(object payload, Context context)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return PreparedRpcRequest.Fail(
                PluginRpcErrors.Unavailable,
                "The plugin RPC invoker has been disposed.");
        }

        if (payload is null)
        {
            return PreparedRpcRequest.Fail(
                PluginRpcErrors.InvalidPayload,
                "An RPC payload is required.");
        }

        if (context is null)
        {
            return PreparedRpcRequest.Fail(
                PluginRpcErrors.InvalidContext,
                "An RPC context is required.");
        }

        try
        {
            var payloadElement = serializer.Serialize(payload, payload.GetType());
            var contextItems = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in context.Items)
            {
                if (item.Key is not string key
                    || IsReservedContextKey(key)
                    || contextItems.ContainsKey(key))
                {
                    return PreparedRpcRequest.Fail(
                        PluginRpcErrors.InvalidContext,
                        "RPC context keys must be unique strings and cannot use Quantum reserved keys.");
                }

                contextItems.Add(
                    key,
                    item.Value is JsonElement element
                        ? element.Clone()
                        : serializer.Serialize(
                            item.Value,
                            item.Value?.GetType() ?? typeof(object)));
            }

            return new PreparedRpcRequest(
                null,
                payloadElement,
                new PluginRpcCallContext(
                    (string)callerPluginId,
                    callerRuntimeId.ToString("N"),
                    contextItems));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return PreparedRpcRequest.Fail(
                PluginRpcErrors.InvalidPayload,
                "The RPC payload or context could not be serialized.");
        }
    }

    internal static bool IsReservedContextKey(string key)
        => string.Equals(key, QuantumRpcContextKeys.CallerPluginId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, QuantumRpcContextKeys.CallerRuntimeId, StringComparison.OrdinalIgnoreCase);

    private sealed record PreparedRpcRequest(
        Result? Failure,
        JsonElement? Payload,
        PluginRpcCallContext? Context)
    {
        public static PreparedRpcRequest Fail(string errorCode, string message)
            => new(Result.Fail(errorCode, message), null, null);
    }
}
