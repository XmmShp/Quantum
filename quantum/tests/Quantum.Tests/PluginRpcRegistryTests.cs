using System.ComponentModel;
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

    [Fact]
    public async Task CatalogExportsDescriptionsSchemasAndAllAttributeMetadata()
    {
        await using var runtime = CreateRuntime("quantum.plugin.a");
        var registry = PluginRpcRegistry.Create([runtime.Runtime], NullLogger.Instance);
        using var callerSerializer = new PluginRpcSerializer();
        using var invoker = new PluginRpcInvoker(
            PluginId.Of("quantum.plugin.caller"),
            Guid.NewGuid(),
            callerSerializer);
        invoker.UseRegistry(registry);

        var result = await invoker.InvokeAsync<System.Text.Json.JsonElement>(
            "quantum.rpc.catalog",
            new { },
            Context.Empty);

        Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal(1, result.Value.GetProperty("schemaVersion").GetInt32());
        var service = result.Value.GetProperty("services")
            .EnumerateArray()
            .Single(item => item.GetProperty("pluginId").GetString() == "quantum.plugin.a"
                && item.GetProperty("serviceName").GetString() == "ordering");
        Assert.Equal("Ordering RPC service.", service.GetProperty("description").GetString());
        Assert.Contains(
            service.GetProperty("attributes").EnumerateArray(),
            attribute => attribute.GetProperty("type").GetString()
                == typeof(CategoryAttribute).FullName);
        var probe = service.GetProperty("attributes")
            .EnumerateArray()
            .Single(attribute => attribute.GetProperty("type").GetString()
                == typeof(RpcCatalogProbeAttribute).FullName);
        Assert.Equal(
            "service",
            probe.GetProperty("constructorArguments")[0].GetProperty("value").GetString());
        Assert.True(
            probe.GetProperty("namedArguments")
                .GetProperty(nameof(RpcCatalogProbeAttribute.Enabled))
                .GetProperty("value")
                .GetBoolean());

        var method = service.GetProperty("methods")
            .EnumerateArray()
            .Single(item => item.GetProperty("methodName").GetString() == nameof(IOrderingRpcService.Ping));
        Assert.Equal("Returns the selected provider.", method.GetProperty("description").GetString());
        Assert.Equal("quantum.plugin.a.ordering.ping", method.GetProperty("qualifiedName").GetString());
        Assert.Contains(
            method.GetProperty("attributes").EnumerateArray(),
            attribute => attribute.GetProperty("type").GetString()
                == typeof(CategoryAttribute).FullName);
        Assert.Contains(
            method.GetProperty("parameterAttributes").EnumerateArray(),
            attribute => attribute.GetProperty("type").GetString()
                == typeof(DescriptionAttribute).FullName);
        Assert.Contains(
            method.GetProperty("returnAttributes").EnumerateArray(),
            attribute => attribute.GetProperty("type").GetString()
                == typeof(DescriptionAttribute).FullName);
        Assert.Equal(
            "Ordering request payload.",
            method.GetProperty("inputSchema").GetProperty("description").GetString());
        Assert.Equal(
            "Selected provider plugin id.",
            method.GetProperty("outputSchema").GetProperty("description").GetString());
        Assert.Equal(
            "Probe input value.",
            method.GetProperty("inputSchema")
                .GetProperty("properties")
                .GetProperty("value")
                .GetProperty("description")
                .GetString());
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

public sealed record OrderingRequest(
    [property: Description("Probe input value.")] string? Value = null);

public sealed record RpcProviderMarker(string PluginId);

[Description("Ordering RPC service.")]
[Category("testing")]
[RpcCatalogProbe("service", Enabled = true)]
[TransportOverQuantum]
[RpcInvocationName("ordering")]
public interface IOrderingRpcService : IRpcService
{
    [Description("Returns the selected provider.")]
    [Category("testing")]
    [return: Description("Selected provider plugin id.")]
    [RpcInvocationName("ping")]
    [RpcInvocationAlias("ordering.alias")]
    Result<string> Ping([Description("Ordering request payload.")] OrderingRequest request);

    Result Reset(OrderingRequest request);
}

[AttributeUsage(AttributeTargets.All)]
public sealed class RpcCatalogProbeAttribute(string label) : Attribute
{
    public string Label { get; } = label;

    public bool Enabled { get; set; }
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
