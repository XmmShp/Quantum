using System.Text.Json;
using NOF.Contract;

namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Declares that an RPC service is transported through the Quantum plugin host.
/// NOF generates the protocol-neutral client interface, while Quantum supplies the
/// name-based transport through <see cref="IRpcInvoker"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class TransportOverQuantumAttribute : TransportOverAttribute;

/// <summary>
/// Replaces the default service or method component used to build a Quantum RPC name.
/// </summary>
[AttributeUsage(
    AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class RpcInvocationNameAttribute : Attribute
{
    public RpcInvocationNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Adds a complete alternative invocation name for an RPC method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RpcInvocationAliasAttribute : Attribute
{
    public RpcInvocationAliasAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Invokes an RPC service implemented by any currently available Quantum plugin.
/// Payloads, contexts, and results cross a serialization boundary even for in-process
/// .NET plugins so that no plugin-owned CLR object escapes its runtime.
/// </summary>
public interface IRpcInvoker
{
    Task<Result<TResponse>> InvokeAsync<TResponse>(
        string rpcName,
        object payload,
        Context context,
        CancellationToken cancellationToken = default);

    Task<Result> InvokeAsync(
        string rpcName,
        object payload,
        Context context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A serializable snapshot of every RPC method currently available through Quantum.
/// </summary>
public sealed record QuantumRpcCatalog(
    int SchemaVersion,
    string CatalogRpcName,
    IReadOnlyList<QuantumRpcServiceInfo> Services);

public sealed record QuantumRpcServiceInfo(
    string PluginId,
    string ServiceName,
    string ServiceType,
    string? Description,
    IReadOnlyList<QuantumRpcAttributeInfo> Attributes,
    IReadOnlyList<QuantumRpcMethodInfo> Methods);

public sealed record QuantumRpcMethodInfo(
    string QualifiedName,
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string Declaration,
    string MethodName,
    string? Description,
    string RequestType,
    string? ResponseType,
    bool ReturnsValue,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    IReadOnlyList<QuantumRpcAttributeInfo> Attributes,
    IReadOnlyList<QuantumRpcAttributeInfo> ParameterAttributes,
    IReadOnlyList<QuantumRpcAttributeInfo> ReturnAttributes);

public sealed record QuantumRpcAttributeInfo(
    string Type,
    IReadOnlyList<QuantumRpcAttributeArgumentInfo> ConstructorArguments,
    IReadOnlyDictionary<string, QuantumRpcAttributeArgumentInfo> NamedArguments);

public sealed record QuantumRpcAttributeArgumentInfo(string Type, JsonElement Value);

public static class QuantumRpcCatalogExtensions
{
    public const string CatalogRpcName = "quantum.rpc.catalog";

    public static Task<Result<QuantumRpcCatalog>> GetCatalogAsync(
        this IRpcInvoker invoker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        return invoker.InvokeAsync<QuantumRpcCatalog>(
            CatalogRpcName,
            new { },
            Context.Empty,
            cancellationToken);
    }
}

/// <summary>
/// Reserved <see cref="Context"/> keys injected by the Quantum host for every RPC call.
/// </summary>
public static class QuantumRpcContextKeys
{
    public const string CallerPluginId = "quantum.rpc.callerPluginId";

    public const string CallerRuntimeId = "quantum.rpc.callerRuntimeId";

    public const string MobileDeviceId = "quantum.mobile.deviceId";
    public const string MobileRequestId = "quantum.mobile.requestId";
    public const string MobileViewId = "quantum.mobile.viewId";
}
