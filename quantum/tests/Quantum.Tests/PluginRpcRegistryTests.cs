using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginRpcRegistryTests
{
    [Fact]
    public async Task ShortNameAndAliasSelectLexicographicallySmallestPluginId()
    {
        await using var last = CreateRuntime("quantum.plugin.z");
        await using var first = CreateRuntime("quantum.plugin.a");
        var registry = PluginRpcRegistry.Create(
            [last.Runtime, first.Runtime],
            NullLogger.Instance);

        var shortName = await InvokeAsync(registry, "ORDERING.PING", first.Serializer);
        var alias = await InvokeAsync(registry, "ordering.alias", first.Serializer);
        var qualified = await InvokeAsync(
            registry,
            "quantum.plugin.z.ordering.ping",
            first.Serializer);

        Assert.Equal("quantum.plugin.a", shortName.Value);
        Assert.Equal("quantum.plugin.a", alias.Value);
        Assert.Equal("quantum.plugin.z", qualified.Value);
    }

    [Fact]
    public async Task MissingNameReturnsFailedResult()
    {
        await using var runtime = CreateRuntime("quantum.plugin.a");
        var registry = PluginRpcRegistry.Create([runtime.Runtime], NullLogger.Instance);

        var invocation = await registry.InvokeAsync(
            "missing.rpc",
            System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            new PluginRpcCallContext("quantum.plugin.caller", Guid.NewGuid().ToString("N"),
                new Dictionary<string, System.Text.Json.JsonElement>()),
            expectsValue: true,
            CancellationToken.None);

        Assert.NotNull(invocation.Failure);
        Assert.Equal("rpc_not_found", invocation.Failure.ErrorCode);
    }

    [Fact]
    public async Task NonGenericRpcInvokerReturnsResultWithoutResponsePayload()
    {
        await using var runtime = CreateRuntime("quantum.plugin.a");
        var registry = PluginRpcRegistry.Create([runtime.Runtime], NullLogger.Instance);
        using var callerSerializer = new PluginRpcSerializer();
        using var invoker = new PluginRpcInvoker(
            PluginId.Of("quantum.plugin.caller"),
            Guid.NewGuid(),
            callerSerializer);
        invoker.UseRegistry(registry);

        var result = await invoker.InvokeAsync(
            "ordering.reset",
            new OrderingRequest(),
            Context.Empty);

        Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.Message}");
    }

    private static async Task<Result<string>> InvokeAsync(
        PluginRpcRegistry registry,
        string rpcName,
        PluginRpcSerializer serializer)
    {
        var invocation = await registry.InvokeAsync(
            rpcName,
            System.Text.Json.JsonSerializer.SerializeToElement(new OrderingRequest()),
            new PluginRpcCallContext("quantum.plugin.caller", Guid.NewGuid().ToString("N"),
                new Dictionary<string, System.Text.Json.JsonElement>()),
            expectsValue: true,
            CancellationToken.None);
        Assert.Null(invocation.Failure);
        return serializer.Deserialize<Result<string>>(invocation.SerializedResult!.Value)!;
    }

    private static RpcRuntimeFixture CreateRuntime(string pluginId)
    {
        var services = new ServiceCollection()
            .AddSingleton(new RpcProviderMarker(pluginId))
            .AddTransient<OrderingRpcServer.Ping, OrderingPing>()
            .AddTransient<OrderingRpcServer.Reset, OrderingReset>()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        var serializer = new PluginRpcSerializer();
        var runtime = PluginRpcRuntime.Create(
            PluginId.Of(pluginId),
            Guid.NewGuid(),
            typeof(OrderingRpcServer).Assembly,
            services.GetRequiredService<IServiceScopeFactory>(),
            serializer,
            NullLogger.Instance);
        runtime.Resume();
        return new RpcRuntimeFixture(runtime, serializer, services);
    }

    private sealed record RpcRuntimeFixture(
        PluginRpcRuntime Runtime,
        PluginRpcSerializer Serializer,
        ServiceProvider Services) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await Services.DisposeAsync();
            Serializer.Dispose();
        }
    }
}

public sealed record OrderingRequest;

public sealed record RpcProviderMarker(string PluginId);

[TransportOverQuantum]
[RpcInvocationName("ordering")]
public interface IOrderingRpcService : IRpcService
{
    [RpcInvocationName("ping")]
    [RpcInvocationAlias("ordering.alias")]
    Result<string> Ping(OrderingRequest request);

    Result Reset(OrderingRequest request);
}

public partial class OrderingRpcServer : RpcServer<IOrderingRpcService>;

public sealed class OrderingPing(RpcProviderMarker provider) : OrderingRpcServer.Ping
{
    public override Task<Result<string>> HandleAsync(
        OrderingRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<string>>(provider.PluginId);
    }
}

public sealed class OrderingReset : OrderingRpcServer.Reset
{
    public override Task<Result> HandleAsync(
        OrderingRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success());
    }
}
