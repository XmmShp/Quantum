using System.Text.Json;
using Microsoft.Extensions.Logging;
using NOF.Contract;

namespace Quantum.Plugins;

internal sealed class PluginRpcRegistry
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<PluginRpcEndpoint>> _routes;

    private PluginRpcRegistry(
        IReadOnlyDictionary<string, IReadOnlyList<PluginRpcEndpoint>> routes)
    {
        _routes = routes;
    }

    public static PluginRpcRegistry Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<PluginRpcEndpoint>>(StringComparer.Ordinal));

    public static PluginRpcRegistry Create(
        IEnumerable<PluginRpcRuntime> runtimes,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(logger);

        var entries = runtimes
            .SelectMany(static runtime => runtime.Methods.SelectMany(method =>
            {
                var endpoint = new PluginRpcEndpoint(runtime, method);
                return new[]
                    {
                        $"{runtime.PluginId}.{method.ServiceName}.{method.MethodName}",
                        $"{method.ServiceName}.{method.MethodName}"
                    }
                    .Concat(method.Aliases)
                    .Select(name => new KeyValuePair<string, PluginRpcEndpoint>(
                        NormalizeName(name),
                        endpoint));
            }))
            .GroupBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PluginRpcEndpoint>)group
                    .Select(static entry => entry.Value)
                    .Distinct()
                    .OrderBy(static endpoint => (string)endpoint.Runtime.PluginId, StringComparer.Ordinal)
                    .ThenBy(static endpoint => endpoint.Method.Declaration, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        foreach (var route in entries.Where(static route => route.Value.Count > 1))
        {
            var selected = route.Value[0];
            logger.LogWarning(
                "RPC name {RpcName} has {ImplementationCount} implementations. "
                + "Plugin {SelectedPluginId} ({SelectedDeclaration}) was selected because its plugin id "
                + "is first in ordinal dictionary order. Candidates: {RpcCandidates}.",
                route.Key,
                route.Value.Count,
                selected.Runtime.PluginId,
                selected.Method.Declaration,
                route.Value.Select(static endpoint => new
                {
                    PluginId = (string)endpoint.Runtime.PluginId,
                    endpoint.Method.Declaration
                }).ToArray());
        }

        return new PluginRpcRegistry(entries);
    }

    public Task<PluginRpcDispatchResult> InvokeAsync(
        string rpcName,
        JsonElement payload,
        PluginRpcCallContext context,
        bool? expectsValue,
        CancellationToken cancellationToken)
    {
        string normalizedName;
        try
        {
            normalizedName = NormalizeName(rpcName);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(PluginRpcDispatchResult.Fail(
                PluginRpcErrors.InvalidName,
                "The RPC name is invalid."));
        }

        if (!_routes.TryGetValue(normalizedName, out var candidates)
            || candidates.FirstOrDefault(static endpoint => endpoint.Runtime.IsActive) is not { } endpoint)
        {
            return Task.FromResult(PluginRpcDispatchResult.Fail(
                PluginRpcErrors.NotFound,
                $"RPC '{normalizedName}' is not implemented by an available plugin."));
        }

        if (expectsValue is { } expected && expected != endpoint.Method.ReturnsValue)
        {
            return Task.FromResult(PluginRpcDispatchResult.Fail(
                PluginRpcErrors.ResponseTypeMismatch,
                expected
                    ? $"RPC '{normalizedName}' does not return Result<T>."
                    : $"RPC '{normalizedName}' does not return Result."));
        }

        return endpoint.Runtime.InvokeAsync(endpoint.Method, payload, context, cancellationToken);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("RPC names cannot contain leading or trailing whitespace.", nameof(name));
        }

        return name.ToLowerInvariant();
    }
}

internal sealed record PluginRpcEndpoint(
    PluginRpcRuntime Runtime,
    PluginRpcMethodDefinition Method);

internal sealed record PluginRpcCallContext(
    string CallerPluginId,
    string CallerRuntimeId,
    IReadOnlyDictionary<string, JsonElement> Items);

internal sealed record PluginRpcDispatchResult(
    Result? Failure,
    JsonElement? SerializedResult,
    bool ReturnsValue)
{
    public static PluginRpcDispatchResult Fail(string errorCode, string message)
        => new(Result.Fail(errorCode, message), null, ReturnsValue: false);

    public static PluginRpcDispatchResult Success(JsonElement result, bool returnsValue)
        => new(null, result, returnsValue);
}

internal static class PluginRpcErrors
{
    public const string InvalidName = "rpc_invalid_name";
    public const string NotFound = "rpc_not_found";
    public const string InvalidPayload = "rpc_invalid_payload";
    public const string InvalidContext = "rpc_invalid_context";
    public const string ResponseTypeMismatch = "rpc_response_type_mismatch";
    public const string InvocationFailed = "rpc_invocation_failed";
    public const string InvalidResult = "rpc_invalid_result";
    public const string Unavailable = "rpc_unavailable";
}
