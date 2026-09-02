using NOF.Contract;
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
