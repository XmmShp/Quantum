using System.Text.Json;
using Microsoft.Extensions.Logging;
using NOF.Contract;

namespace Quantum.Plugins;

internal sealed class PluginRpcRegistry
{
    private const string CatalogRpcName = "quantum.rpc.catalog";
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, IReadOnlyList<PluginRpcEndpoint>> _routes;
    private readonly IReadOnlyList<PluginRpcEndpoint> _endpoints;

    private PluginRpcRegistry(
        IReadOnlyDictionary<string, IReadOnlyList<PluginRpcEndpoint>> routes,
        IReadOnlyList<PluginRpcEndpoint> endpoints)
    {
        _routes = routes;
        _endpoints = endpoints;
    }

    public static PluginRpcRegistry Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<PluginRpcEndpoint>>(StringComparer.Ordinal),
        []);

    public static PluginRpcRegistry Create(
        IEnumerable<PluginRpcRuntime> runtimes,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(logger);

        var endpoints = runtimes
            .SelectMany(static runtime => runtime.Methods.Select(method =>
                new PluginRpcEndpoint(runtime, method)))
            .ToArray();
        var entries = endpoints
            .SelectMany(static endpoint =>
            {
                return new[]
                    {
                        $"{endpoint.Runtime.PluginId}.{endpoint.Method.ServiceName}.{endpoint.Method.MethodName}",
                        $"{endpoint.Method.ServiceName}.{endpoint.Method.MethodName}"
                    }
                    .Concat(endpoint.Method.Aliases)
                    .Select(name => new KeyValuePair<string, PluginRpcEndpoint>(
                        NormalizeName(name),
                        endpoint));
            })
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

        return new PluginRpcRegistry(entries, endpoints);
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

        if (string.Equals(normalizedName, CatalogRpcName, StringComparison.Ordinal))
        {
            if (expectsValue == false)
            {
                return Task.FromResult(PluginRpcDispatchResult.Fail(
                    PluginRpcErrors.ResponseTypeMismatch,
                    $"RPC '{CatalogRpcName}' returns Result<T>."));
            }

            var result = Result.Success(CreateCatalog());
            return Task.FromResult(PluginRpcDispatchResult.Success(
                JsonSerializer.SerializeToElement(result, result.GetType(), CatalogJsonOptions),
                returnsValue: true));
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

    private PluginRpcCatalogDocument CreateCatalog()
    {
        var pluginServices = _endpoints
            .Where(static endpoint => endpoint.Runtime.IsActive)
            .GroupBy(
                static endpoint => new
                {
                    PluginId = (string)endpoint.Runtime.PluginId,
                    endpoint.Method.Catalog.ServiceName,
                    endpoint.Method.Catalog.ServiceType
                })
            .Select(static group => new PluginRpcCatalogService(
                group.Key.PluginId,
                group.Key.ServiceName,
                group.Key.ServiceType,
                group.First().Method.Catalog.ServiceDescription,
                group.First().Method.Catalog.ServiceAttributes,
                group
                    .OrderBy(static endpoint => endpoint.Method.CanonicalName, StringComparer.Ordinal)
                    .Select(static endpoint => endpoint.Method.Catalog.Method with
                    {
                        QualifiedName = $"{endpoint.Runtime.PluginId}.{endpoint.Method.CanonicalName}",
                        CanonicalName = endpoint.Method.CanonicalName,
                        Aliases = endpoint.Method.Aliases
                    })
                    .ToArray()))
            .OrderBy(static service => service.PluginId, StringComparer.Ordinal)
            .ThenBy(static service => service.ServiceName, StringComparer.Ordinal)
            .ToArray();
        return new PluginRpcCatalogDocument(
            SchemaVersion: 1,
            CatalogRpcName,
            [CreateHostCatalogService(), .. pluginServices]);
    }

    private static PluginRpcCatalogService CreateHostCatalogService()
        => new(
            PluginId: "quantum.host",
            ServiceName: "quantum.rpc",
            ServiceType: "Quantum.Host.RpcCatalog",
            Description: "Exposes all currently available Quantum RPC methods for discovery and AI tools.",
            Attributes: [],
            Methods:
            [
                new PluginRpcCatalogMethod(
                    QualifiedName: CatalogRpcName,
                    CanonicalName: CatalogRpcName,
                    Aliases: [],
                    Declaration: "Quantum.Host.RpcCatalog.GetCatalog",
                    MethodName: "GetCatalog",
                    Description: "Returns the RPC catalog, descriptions, JSON schemas, and attribute metadata.",
                    RequestType: "System.Object",
                    ResponseType: "Quantum.RpcCatalog",
                    ReturnsValue: true,
                    InputSchema: JsonSerializer.SerializeToElement(
                        new
                        {
                            type = "object",
                            properties = new { },
                            additionalProperties = false
                        },
                        CatalogJsonOptions),
                    OutputSchema: JsonSerializer.SerializeToElement(
                        new
                        {
                            type = "object",
                            dotnetType = "Quantum.RpcCatalog",
                            description = "RPC catalog document with schemaVersion, catalogRpcName, and services."
                        },
                        CatalogJsonOptions),
                    Attributes: [],
                    ParameterAttributes: [],
                    ReturnAttributes: [])
            ]);

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
