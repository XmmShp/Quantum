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
/// Reserved <see cref="Context"/> keys injected by the Quantum host for every RPC call.
/// </summary>
public static class QuantumRpcContextKeys
{
    public const string CallerPluginId = "quantum.rpc.callerPluginId";

    public const string CallerRuntimeId = "quantum.rpc.callerRuntimeId";
}
