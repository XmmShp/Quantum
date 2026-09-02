using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugins;
using Quantum.WebPlugins;

namespace Quantum.Tests;

public sealed class WebPluginInteropBridgeTests
{
    private const string PluginId = "quantum.plugin.web";
    private const string TargetPluginId = "quantum.plugin.target";
    private const string Topic = "example.web.status";

    [Fact]
    public async Task ReplacementPublishIgnoresDeliveryFailureFromStaleRuntime()
    {
        await using var host = CreateHost();
        var oldRuntimeId = Guid.NewGuid();
        var newRuntimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([LoadedWebPlugin(oldRuntimeId)]);
        var javaScript = new RejectingEventDeliveryJavaScriptRuntime();
        using var rpcRouter = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        using var bridge = CreateBridge(catalog, rpcRouter, host, javaScript);
        await SubscribeAsync(bridge, oldRuntimeId);

        catalog.Replace([LoadedWebPlugin(newRuntimeId)]);

        var result = await PublishAsync(bridge, newRuntimeId);

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
        Assert.Equal(1, javaScript.DispatchCount);
    }

    [Fact]
    public async Task ActiveRuntimePublishStillReportsDeliveryFailure()
    {
        await using var host = CreateHost();
        var runtimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([LoadedWebPlugin(runtimeId)]);
        var javaScript = new RejectingEventDeliveryJavaScriptRuntime();
        using var rpcRouter = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        using var bridge = CreateBridge(catalog, rpcRouter, host, javaScript);
        await SubscribeAsync(bridge, runtimeId);

        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await PublishAsync(bridge, runtimeId));

        Assert.Contains(exception.InnerExceptions, static failure => failure is JSException);
        Assert.Equal(1, javaScript.DispatchCount);
    }

    [Fact]
    public async Task RpcInvocationDoesNotRequireAnIntegrationDeclaration()
    {
        await using var host = CreateHost();
        await using var target = CreateRpcTarget(host);
        var runtimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([
            LoadedWebPlugin(runtimeId),
            target.Plugin
        ]);
        using var rpcRouter = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        using var bridge = CreateBridge(
            catalog,
            rpcRouter,
            host,
            new RejectingEventDeliveryJavaScriptRuntime());

        var result = await bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "rpc",
            "invoke",
            JsonSerializer.SerializeToElement(new
            {
                rpcName = "TEST.ECHO",
                payload = new { value = "hello" },
                context = new { trace = "web-test" }
            }));

        Assert.True(result.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(
            $"{PluginId}:echo:hello",
            result.GetProperty("value").GetString());
    }

    [Fact]
    public async Task MissingRpcReturnsFailedResultInsteadOfThrowing()
    {
        await using var host = CreateHost();
        var runtimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([LoadedWebPlugin(runtimeId)]);
        using var rpcRouter = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        using var bridge = CreateBridge(
            catalog,
            rpcRouter,
            host,
            new RejectingEventDeliveryJavaScriptRuntime());

        var result = await bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "rpc",
            "invoke",
            JsonSerializer.SerializeToElement(new
            {
                rpcName = "missing.service.call",
                payload = new { },
                context = new { }
            }));

        Assert.False(result.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("rpc_not_found", result.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task EnvironmentSnapshotReportsVersionRangeAndCompatibility()
    {
        await using var host = CreateHost();
        var runtimeId = Guid.NewGuid();
        await using var target = CreateRpcTarget(host);
        var catalog = new PluginCatalog([
            LoadedWebPlugin(
                runtimeId,
                [new PluginIntegration(
                    Quantum.Plugin.Abstraction.PluginId.Of(TargetPluginId),
                    VersionRange.Of("[1.0.0,2.0.0)"))]),
            target.Plugin
        ]);
        using var rpcRouter = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        using var bridge = CreateBridge(
            catalog,
            rpcRouter,
            host,
            new RejectingEventDeliveryJavaScriptRuntime());

        var result = await bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "environment",
            "snapshot",
            JsonSerializer.SerializeToElement(new { }));

        var integration = Assert.Single(result.GetProperty("integrations").EnumerateArray());
        Assert.Equal(TargetPluginId, integration.GetProperty("pluginId").GetString());
        Assert.Equal("[1.0.0,2.0.0)", integration.GetProperty("versionRange").GetString());
        Assert.True(integration.GetProperty("active").GetBoolean());
        Assert.False(integration.TryGetProperty("minimumVersion", out _));
    }

    private static ServiceProvider CreateHost()
        => new ServiceCollection()
            .AddQuantumPluginEventBus()
            .AddTransient<WebBridgeTestRpcService.Echo, WebBridgeEcho>()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

    private static WebPluginInteropBridge CreateBridge(
        PluginCatalog catalog,
        PluginRpcRouter rpcRouter,
        ServiceProvider host,
        IJSRuntime javaScript)
        => new(
            catalog,
            rpcRouter,
            new TestNavigationManager(),
            javaScript,
            host.GetRequiredService<QuantumPluginEventBusFactory>(),
            NullLogger<WebPluginInteropBridge>.Instance);

    private static Task<JsonElement> SubscribeAsync(
        WebPluginInteropBridge bridge,
        Guid runtimeId)
        => bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "eventBus",
            "subscribe",
            JsonSerializer.SerializeToElement(new
            {
                topic = Topic,
                subscriptionId = "subscription"
            }));

    private static Task<JsonElement> PublishAsync(
        WebPluginInteropBridge bridge,
        Guid runtimeId)
        => bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "eventBus",
            "publish",
            JsonSerializer.SerializeToElement(new
            {
                topic = Topic,
                payload = new { state = "activated" }
            }));

    private static LoadedPlugin LoadedWebPlugin(
        Guid runtimeId,
        IEnumerable<PluginIntegration>? integrations = null)
        => new(
            new PluginManifest(
                Quantum.Plugin.Abstraction.PluginId.Of(PluginId),
                SemanticVersion.Of("1.0.0"),
                PluginRuntimeDefinition.Web("plugin.js"),
                integrations: integrations),
            Path.Combine("plugins", PluginId),
            entryAssembly: null,
            routes: [],
            runtimeId);

    private static RpcTarget CreateRpcTarget(ServiceProvider services)
    {
        var runtimeId = Guid.NewGuid();
        var serializer = new PluginRpcSerializer();
        var rpcRuntime = PluginRpcRuntime.Create(
            Quantum.Plugin.Abstraction.PluginId.Of(TargetPluginId),
            runtimeId,
            typeof(WebBridgeTestRpcService).Assembly,
            services.GetRequiredService<IServiceScopeFactory>(),
            serializer,
            NullLogger.Instance);
        rpcRuntime.Resume();
        var plugin = new LoadedPlugin(
            new PluginManifest(
                Quantum.Plugin.Abstraction.PluginId.Of(TargetPluginId),
                SemanticVersion.Of("1.0.0"),
                "Target.dll"),
            Path.Combine("plugins", TargetPluginId),
            typeof(WebPluginInteropBridgeTests).Assembly,
            routes: [],
            runtimeId,
            services)
        {
            RpcRuntime = rpcRuntime
        };
        return new RpcTarget(plugin, rpcRuntime, serializer);
    }

    private sealed record RpcTarget(
        LoadedPlugin Plugin,
        PluginRpcRuntime Runtime,
        PluginRpcSerializer Serializer) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            Serializer.Dispose();
        }
    }

    private sealed class RejectingEventDeliveryJavaScriptRuntime : IJSRuntime
    {
        public int DispatchCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Assert.Equal("quantum.plugins.dispatchEvent", identifier);
            DispatchCount++;
            return ValueTask.FromException<TValue>(new JSException("delivery failed"));
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://quantum.local/", "https://quantum.local/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}

public sealed record WebBridgeEchoRequest(string Value);

[TransportOverQuantum]
[RpcInvocationName("test")]
public interface IWebBridgeTestRpcService : IRpcService
{
    [RpcInvocationName("echo")]
    Result<string> Echo(WebBridgeEchoRequest request);
}

public partial class WebBridgeTestRpcService : RpcServer<IWebBridgeTestRpcService>;

public sealed class WebBridgeEcho : WebBridgeTestRpcService.Echo
{
    public override Task<Result<string>> HandleAsync(
        WebBridgeEchoRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var caller = context[QuantumRpcContextKeys.CallerPluginId] as string ?? "unknown";
        return Task.FromResult<Result<string>>($"{caller}:echo:{request.Value}");
    }
}
