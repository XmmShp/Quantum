using NOF.Contract;
using System.Text.Json;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class PluginRpcContractTests
{
    [Fact]
    public void RpcInvokerIsAnInterface()
    {
        Assert.True(typeof(IRpcInvoker).IsInterface);
        Assert.Equal(
            2,
            typeof(IRpcInvoker).GetMethods()
                .Count(static method => method.Name == nameof(IRpcInvoker.InvokeAsync)));
    }

    [Fact]
    public void TransportGeneratesOnlyTheProtocolNeutralClientInterface()
    {
        Assert.True(typeof(IRpcProbeServiceClient).IsInterface);
        Assert.Contains(
            typeof(IRpcProbeServiceClient).GetInterfaces(),
            static implemented => implemented == typeof(IRpcClient<IRpcProbeService>));
        Assert.DoesNotContain(
            typeof(PluginRpcContractTests).Assembly.GetTypes(),
            static type => type.Name is "HttpRpcProbeServiceClient"
                or "LocalRpcProbeServiceClient"
                or "QuantumRpcProbeServiceClient");
    }

    [Fact]
    public void InvocationMetadataSupportsServiceMethodAndMultipleAliases()
    {
        var serviceName = Assert.Single(
            typeof(IRpcProbeService).GetCustomAttributes(typeof(RpcInvocationNameAttribute), false));
        Assert.Equal("probe", Assert.IsType<RpcInvocationNameAttribute>(serviceName).Name);

        var method = typeof(IRpcProbeService).GetMethod(nameof(IRpcProbeService.Ping));
        Assert.NotNull(method);
        Assert.Equal(
            ["probe.lookup", "sample.lookup"],
            method.GetCustomAttributes(typeof(RpcInvocationAliasAttribute), false)
                .Cast<RpcInvocationAliasAttribute>()
                .Select(static attribute => attribute.Name));
    }

    [Fact]
    public async Task CatalogHelperUsesTheBuiltInDiscoveryRpc()
    {
        var invoker = new RecordingRpcInvoker();

        var result = await invoker.GetCatalogAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuantumRpcCatalogExtensions.CatalogRpcName, invoker.RpcName);
        Assert.Equal(Context.Empty, invoker.Context);
        Assert.Equal("{}", JsonSerializer.Serialize(invoker.Payload));
    }

    private sealed class RecordingRpcInvoker : IRpcInvoker
    {
        public string? RpcName { get; private set; }

        public object? Payload { get; private set; }

        public Context? Context { get; private set; }

        public Task<Result<TResponse>> InvokeAsync<TResponse>(
            string rpcName,
            object payload,
            Context context,
            CancellationToken cancellationToken = default)
        {
            RpcName = rpcName;
            Payload = payload;
            Context = context;
            object catalog = new QuantumRpcCatalog(1, rpcName, []);
            return Task.FromResult(Result.Success((TResponse)catalog));
        }

        public Task<Result> InvokeAsync(
            string rpcName,
            object payload,
            Context context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}

public sealed record RpcProbeRequest(string Value);

[TransportOverQuantum]
[RpcInvocationName("probe")]
public interface IRpcProbeService : IRpcService
{
    [RpcInvocationName("ping")]
    [RpcInvocationAlias("probe.lookup")]
    [RpcInvocationAlias("sample.lookup")]
    Result<string> Ping(RpcProbeRequest request);
}
